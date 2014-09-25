﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using GrobExp.Compiler;
using GrobExp.Mutators.AutoEvaluators;
using GrobExp.Mutators.CustomFields;

namespace GrobExp.Mutators
{
    public abstract class ConverterCollection<TSource, TDest> : IConverterCollection<TSource, TDest> where TDest : new()
    {
        protected ConverterCollection(IPathFormatterCollection pathFormatterCollection)
        {
            this.pathFormatterCollection = pathFormatterCollection;
        }

        public Func<TSource, TDest> GetConverter(MutatorsContext context)
        {
            return GetOrCreateHashtableSlot(context).Converter;
        }

        public Action<TSource, TDest> GetMerger(MutatorsContext context)
        {
            return GetOrCreateHashtableSlot(context).Merger;
        }

        public MutatorsTree<TSource> Migrate(MutatorsTree<TDest> mutatorsTree, MutatorsContext context)
        {
            return mutatorsTree == null ? null : mutatorsTree.Migrate<TSource>(GetOrCreateHashtableSlot(context).ConverterTree);
        }

        public MutatorsTree<TSource> GetValidationsTree(MutatorsContext context, int priority)
        {
            return new SimpleMutatorsTree<TSource>(GetOrCreateHashtableSlot(context).ValidationsTree, pathFormatterCollection.GetPathFormatter<TSource>(), pathFormatterCollection, priority);
        }

        public MutatorsTree<TDest> MigratePaths(MutatorsTree<TDest> mutatorsTree, MutatorsContext context)
        {
            return mutatorsTree == null ? null : mutatorsTree.MigratePaths<TSource>(GetOrCreateHashtableSlot(context).ConverterTree);
        }

        protected abstract void Configure(MutatorsContext context, ConverterConfigurator<TSource, TDest> configurator);

        protected virtual void BeforeConvert(TSource source)
        {
        }

        protected virtual void AfterConvert(TDest dest, TSource source)
        {
        }

        private HashtableSlot GetOrCreateHashtableSlot(MutatorsContext context)
        {
            var key = context.GetKey();
            var slot = (HashtableSlot)hashtable[key];
            if(slot == null)
            {
                lock(lockObject)
                {
                    slot = (HashtableSlot)hashtable[key];
                    if(slot == null)
                    {
                        var tree = ModelConfigurationNode.CreateRoot(typeof(TDest));
                        ConfigureInternal(context, new ConverterConfigurator<TSource, TDest>(tree));
                        var validationsTree = ModelConfigurationNode.CreateRoot(typeof(TSource));
                        tree.ExtractValidationsFromConverters(validationsTree);
                        var treeMutator = (Expression<Action<TDest, TSource>>)tree.BuildTreeMutator(typeof(TSource));
                        var compiledTreeMutator = LambdaCompiler.Compile(treeMutator, CompilerOptions.All);
                        hashtable[key] = slot = new HashtableSlot
                            {
                                ConverterTree = tree,
                                ValidationsTree = validationsTree,
                                Converter = (source =>
                                    {
                                        var dest = new TDest();
                                        BeforeConvert(source);
                                        compiledTreeMutator(dest, source);
                                        AfterConvert(dest, source);
                                        return dest;
                                    }),
                                Merger = ((source, dest) =>
                                    {
                                        BeforeConvert(source);
                                        compiledTreeMutator(dest, source);
                                        AfterConvert(dest, source);
                                    })
                            };
                    }
                }
            }
            return slot;
        }

        private static TypeCode GetTypeCode(Type type)
        {
            return type.IsNullable() ? GetTypeCode(type.GetGenericArguments()[0]) : Type.GetTypeCode(type);
        }

        private static bool IsALeaf(Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type.IsValueType;
        }

        private static void FindCustomFieldsContainer(Type type, Expression current, List<KeyValuePair<PropertyInfo, Expression>> result)
        {
            if(type == null || IsALeaf(type))
                return;
            var properties = type.GetOrderedProperties();
            foreach(var property in properties)
            {
                var next = Expression.Property(current, property);
                if(property.GetCustomAttributes(typeof(CustomFieldsContainerAttribute), false).Any())
                    result.Add(new KeyValuePair<PropertyInfo, Expression>(property, next));
                else
                    FindCustomFieldsContainer(property.PropertyType, next, result);
            }
        }

        private static PropertyInfo FindCustomFieldsContainer(Type type, out LambdaExpression path)
        {
            var parameter = Expression.Parameter(type);
            var customFieldsContainers = new List<KeyValuePair<PropertyInfo, Expression>>();
            FindCustomFieldsContainer(type, parameter, customFieldsContainers);
            switch(customFieldsContainers.Count)
            {
            case 0:
                path = null;
                return null;
            case 1:
                path = Expression.Lambda(customFieldsContainers[0].Value, parameter);
                return customFieldsContainers[0].Key;
            default:
                throw new InvalidOperationException("Found more than one custom fields container in type '" + type + "'");
            }
        }

        private static void FindCustomFields(Type type, Expression current, string path, PropertyInfo rootProperty, List<Tuple<string, PropertyInfo, Expression>> result)
        {
            var properties = type.GetOrderedProperties().Where(prop => prop.GetCustomAttributes(typeof(CustomFieldAttribute), false).Any()).ToArray();
            foreach(var property in properties)
            {
                var nextCurrent = Expression.Property(current, property);
                var nextPath = string.IsNullOrEmpty(path) ? property.Name : path + "ё" + property.Name;
                if(string.IsNullOrEmpty(path))
                    rootProperty = property;
                if(IsALeaf(property.PropertyType))
                    result.Add(new Tuple<string, PropertyInfo, Expression>(nextPath, rootProperty, nextCurrent));
                else
                    FindCustomFields(property.PropertyType, nextCurrent, nextPath, rootProperty, result);
            }
        }

        private static Tuple<string, PropertyInfo, LambdaExpression>[] FindCustomFields(Type type)
        {
            var parameter = Expression.Parameter(type);
            var customFields = new List<Tuple<string, PropertyInfo, Expression>>();
            FindCustomFields(type, parameter, "", null, customFields);
            return customFields.Select(tuple => new Tuple<string, PropertyInfo, LambdaExpression>(tuple.Item1, tuple.Item2, Expression.Lambda(tuple.Item3, parameter))).ToArray();
        }

        private static void ConfigureCustomFields(ConverterConfigurator<TSource, TDest> configurator, LambdaExpression pathToSourceChild, LambdaExpression pathToDestChild)
        {
            var sourceChildType = pathToSourceChild.Body.Type;
            var destChildType = pathToDestChild.Body.Type;
            LambdaExpression pathToSourceCustomFieldsContainer;
            var sourceCustomFieldsContainer = FindCustomFieldsContainer(sourceChildType, out pathToSourceCustomFieldsContainer);
            var sourceCustomFields = FindCustomFields(sourceChildType);
            LambdaExpression pathToDestCustomFieldsContainer;
            var destCustomFieldsContainer = FindCustomFieldsContainer(destChildType, out pathToDestCustomFieldsContainer);
            var destCustomFields = FindCustomFields(destChildType);
            if(sourceCustomFields.Length > 0)
            {
                if(destCustomFields.Length > 0 || sourceCustomFieldsContainer != null)
                    throw new InvalidOperationException();
                if(destCustomFieldsContainer == null)
                    return;
                var destParameter = pathToDestCustomFieldsContainer.Parameters.Single();
                var indexerGetter = destCustomFieldsContainer.PropertyType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance).GetGetMethod();
                foreach(var customField in sourceCustomFields)
                {
                    var path = customField.Item1;
                    var property = customField.Item2;
                    var value = customField.Item3;
                    var pathToTarget = pathToDestChild.Merge(Expression.Lambda(Expression.Call(pathToDestCustomFieldsContainer.Body, indexerGetter, Expression.Constant(path)), destParameter)).Body;
                    configurator.SetMutator(Expression.Property(pathToTarget, "TypeCode"), EqualsToConfiguration.Create<TDest>(Expression.Lambda(Expression.Constant(GetTypeCode(value.Body.Type)))));
                    configurator.SetMutator(Expression.Property(pathToTarget, "Value"), EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(value)));
                    var customFieldAttribute = property.GetCustomAttributes(typeof(CustomFieldAttribute), false).SingleOrDefault() as CustomFieldAttribute;
                    if(customFieldAttribute != null && customFieldAttribute.TitleType != null)
                        configurator.SetMutator(Expression.Property(pathToTarget, "Title"), EqualsToConfiguration.Create<TDest>(Expression.Lambda(Expression.New(customFieldAttribute.TitleType))));
                }
            }
            else if(destCustomFields.Length > 0)
            {
                if(destCustomFieldsContainer != null)
                    throw new InvalidOperationException();
                if(sourceCustomFieldsContainer == null)
                    return;
                var sourceParameter = pathToSourceCustomFieldsContainer.Parameters.Single();
                var indexerGetter = sourceCustomFieldsContainer.PropertyType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance).GetGetMethod();
                foreach(var customField in destCustomFields)
                {
                    var pathToTarget = pathToDestChild.Merge(customField.Item3).Body;
                    var value = Expression.Convert(Expression.Coalesce(Expression.Property(Expression.Call(pathToSourceCustomFieldsContainer.Body, indexerGetter, Expression.Constant(customField.Item1)), "Value"), Expression.Convert(Expression.Default(pathToTarget.Type), typeof(object))), pathToTarget.Type);
                    configurator.SetMutator(pathToTarget, EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(Expression.Lambda(value, sourceParameter))));
                }
            }
            else
            {
                if(sourceCustomFieldsContainer == null || destCustomFieldsContainer == null)
                    return;
                var destParameter = pathToDestCustomFieldsContainer.Parameters.Single();
                var sourceParameter = pathToSourceCustomFieldsContainer.Parameters.Single();
                Expression pathToDestCustomContainer = Expression.Call(MutatorsHelperFunctions.EachMethod.MakeGenericMethod(destCustomFieldsContainer.PropertyType.GetItemType()), pathToDestCustomFieldsContainer.Body);
                Expression pathToSourceCustomContainer = Expression.Call(MutatorsHelperFunctions.EachMethod.MakeGenericMethod(sourceCustomFieldsContainer.PropertyType.GetItemType()), pathToSourceCustomFieldsContainer.Body);
                Expression pathToTarget = pathToDestChild.Merge(Expression.Lambda(Expression.Property(pathToDestCustomContainer, "Key"), destParameter)).Body;
                Expression value = Expression.Property(pathToSourceCustomContainer, "Key");
                configurator.SetMutator(pathToTarget, EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(Expression.Lambda(value, sourceParameter))));
                value = Expression.Property(pathToSourceCustomContainer, "Value");
                pathToTarget = pathToDestChild.Merge(Expression.Lambda(Expression.Property(pathToDestCustomContainer, "Value"), destParameter)).Body;
                configurator.SetMutator(Expression.Property(pathToTarget, "TypeCode"), EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(Expression.Lambda(Expression.Property(value, "TypeCode"), sourceParameter))));
                configurator.SetMutator(Expression.Property(pathToTarget, "Value"), EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(Expression.Lambda(Expression.Property(value, "Value"), sourceParameter))));
                configurator.SetMutator(Expression.Property(pathToTarget, "Title"), EqualsToConfiguration.Create<TDest>(pathToSourceChild.Merge(Expression.Lambda(Expression.Property(value, "Title"), sourceParameter))));
            }
        }

        private static void ConfigureCustomFields(ConverterConfigurator<TSource, TDest> configurator)
        {
            var sourceParameter = Expression.Parameter(typeof(TSource));
            var destParameter = Expression.Parameter(typeof(TDest));
            ConfigureCustomFields(configurator, Expression.Lambda(sourceParameter, sourceParameter), Expression.Lambda(destParameter, destParameter));
            ConfigureCustomFieldsForArrays(configurator, typeof(TDest), Expression.Lambda(destParameter, destParameter));
        }

        private static void ConfigureCustomFieldsForArrays(ConverterConfigurator<TSource, TDest> configurator, Type type, LambdaExpression pathToDestChild)
        {
            if(type == null || IsALeaf(type))
                return;
            var tree = configurator.GetTree();
            var properties = type.GetOrderedProperties();
            var parameter = Expression.Parameter(type);
            foreach(var property in properties)
            {
                var pathToNextDestChild = pathToDestChild.Merge(Expression.Lambda(Expression.Property(parameter, property), parameter));
                if(!property.PropertyType.IsArray)
                    ConfigureCustomFieldsForArrays(configurator, property.PropertyType, pathToNextDestChild);
                else
                {
                    var pathToDestArray = pathToNextDestChild.Body;
                    var node = tree.Traverse(pathToDestArray, false);
                    if(node == null)
                        continue;
                    var arrays = node.GetArrays(true);
                    Expression pathToSourceArray;
                    if(!arrays.TryGetValue(typeof(TSource), out pathToSourceArray))
                        continue;
                    var pathToDestArrayItem = Expression.Call(MutatorsHelperFunctions.EachMethod.MakeGenericMethod(pathToDestArray.Type.GetItemType()), pathToDestArray);
                    var pathToSourceArrayItem = Expression.Call(MutatorsHelperFunctions.EachMethod.MakeGenericMethod(pathToSourceArray.Type.GetItemType()), pathToSourceArray);
                    ConfigureCustomFields(configurator, Expression.Lambda(pathToSourceArrayItem, pathToSourceArray.ExtractParameters()), Expression.Lambda(pathToDestArrayItem, pathToDestArray.ExtractParameters()));
                }
            }
        }

        private void ConfigureInternal(MutatorsContext context, ConverterConfigurator<TSource, TDest> configurator)
        {
            Configure(context, configurator);
            ConfigureCustomFields(configurator);
        }

        private readonly IPathFormatterCollection pathFormatterCollection;

        private readonly object lockObject = new object();

        private readonly Hashtable hashtable = new Hashtable();

        private class HashtableSlot
        {
            public ModelConfigurationNode ConverterTree { get; set; }
            public ModelConfigurationNode ValidationsTree { get; set; }
            public Func<TSource, TDest> Converter { get; set; }
            public Action<TSource, TDest> Merger { get; set; }
        }
    }
}