﻿using System;
using System.Linq.Expressions;

using GrobExp.Mutators.ModelConfiguration;
using GrobExp.Mutators.Visitors;

namespace GrobExp.Mutators
{
    public class ConverterConfigurator<TSource, TDest>
    {
        public ConverterConfigurator(ModelConfigurationNode root, LambdaExpression condition = null)
        {
            Condition = condition;
            this.root = root;
        }

        public void SetMutator(Expression pathToTarget, MutatorConfiguration mutator)
        {
            root.Traverse(pathToTarget.ResolveInterfaceMembers(), true).AddMutator(Condition == null ? mutator : mutator.If(Condition));
        }

        public ConverterConfigurator<TSource, TDest> WithoutCondition()
        {
            return new ConverterConfigurator<TSource, TDest>(root);
        }

        public ConverterConfigurator<TSource, TSource, TDest, TDest, TValue> Target<TValue>(Expression<Func<TDest, TValue>> pathToValue)
        {
            return new ConverterConfigurator<TSource, TSource, TDest, TDest, TValue>(root, source => source, dest => dest, pathToValue, Condition);
        }

        public ConverterConfigurator<TSource, TSource, TDest, TChild, TChild> GoTo<TChild>(Expression<Func<TDest, TChild>> pathToChild)
        {
            return new ConverterConfigurator<TSource, TSource, TDest, TChild, TChild>(root, source => source, pathToChild, pathToChild, Condition);
        }

        public ConverterConfigurator<TSource, TSourceChild, TDest, TDestChild, TDestChild> GoTo<TDestChild, TSourceChild>(Expression<Func<TDest, TDestChild>> pathToDestChild, Expression<Func<TSource, TSourceChild>> pathToSourceChild)
        {
            return new ConverterConfigurator<TSource, TSourceChild, TDest, TDestChild, TDestChild>(root, pathToSourceChild, pathToDestChild, pathToDestChild, Condition);
        }

        public ConverterConfigurator<TSource, TDest> If(LambdaExpression condition)
        {
            var preparedCondition = (LambdaExpression)new MethodReplacer(MutatorsHelperFunctions.EachMethod, MutatorsHelperFunctions.CurrentMethod).Visit(condition);
            return new ConverterConfigurator<TSource, TDest>(root, Condition.AndAlso(preparedCondition));
        }

        public ConverterConfigurator<TSource, TDest> If(Expression<Func<TSource, bool?>> condition) => If((LambdaExpression)condition);

        public ConverterConfigurator<TSource, TDest> If(Expression<Func<TSource, TDest, bool?>> condition) => If((LambdaExpression)condition);

        public LambdaExpression Condition { get; }

        internal ModelConfigurationNode GetTree() => root;

        private readonly ModelConfigurationNode root;
    }

    public class ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue>
    {
        public ConverterConfigurator(ModelConfigurationNode root, Expression<Func<TSourceRoot, TSourceChild>> pathToSourceChild, Expression<Func<TDestRoot, TDestChild>> pathToChild, Expression<Func<TDestRoot, TDestValue>> pathToValue, LambdaExpression condition)
        {
            this.root = root;
            PathToSourceChild = pathToSourceChild;
            PathToChild = pathToChild;
            PathToValue = pathToValue;
            Condition = condition;
        }

        public void SetMutator(MutatorConfiguration mutator)
        {
            var rootMutator = GetRootMutator(mutator);
            if (PathToValue != null)
                root.AddMutatorSmart(PathToValue.ResolveInterfaceMembers(), Condition == null ? rootMutator : rootMutator.If(Condition));
        }

        /// <summary>
        ///     В случае, когда нахерачили всяких GoTo, пути в конфигурации могут идти не от рута. Здесь это фиксится.
        /// </summary>
        private MutatorConfiguration GetRootMutator(MutatorConfiguration mutator)
        {
            if (mutator.Type == typeof(TDestRoot))
                return mutator;

            var pathToChild = PathToChild.ReplaceMethod(MutatorsHelperFunctions.EachMethod, MutatorsHelperFunctions.CurrentMethod).ResolveInterfaceMembers();
            return mutator.ToRoot((Expression<Func<TDestRoot, TDestChild>>)pathToChild);
        }

        public ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue> WithoutCondition()
        {
            return new ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue>(root, PathToSourceChild, PathToChild, PathToValue, null);
        }

        public ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, T> Target<T>(Expression<Func<TDestValue, T>> pathToValue)
        {
            return new ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, T>(root, PathToSourceChild, PathToChild, PathToValue.Merge(pathToValue), Condition);
        }

        public ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, T, T> GoTo<T>(Expression<Func<TDestChild, T>> pathToChild)
        {
            var path = PathToChild.Merge(pathToChild);
            return new ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, T, T>(root, PathToSourceChild, path, path, Condition);
        }

        public ConverterConfigurator<TSourceRoot, T2, TDestRoot, T1, T1> GoTo<T1, T2>(Expression<Func<TDestChild, T1>> pathToDestChild, Expression<Func<TSourceChild, T2>> pathToSourceChild)
        {
            var path = PathToChild.Merge(pathToDestChild);
            return new ConverterConfigurator<TSourceRoot, T2, TDestRoot, T1, T1>(root, PathToSourceChild.Merge(pathToSourceChild), path, path, Condition);
        }

        public ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue> If(Expression<Func<TSourceChild, bool?>> condition)
        {
            return new ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue>(root, PathToSourceChild, PathToChild, PathToValue, Condition.AndAlso((LambdaExpression)new MethodReplacer(MutatorsHelperFunctions.EachMethod, MutatorsHelperFunctions.CurrentMethod).Visit(PathToSourceChild.Merge(condition))));
        }

        public ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue> If(Expression<Func<TSourceChild, TDestChild, bool?>> condition)
        {
            return new ConverterConfigurator<TSourceRoot, TSourceChild, TDestRoot, TDestChild, TDestValue>(root, PathToSourceChild, PathToChild, PathToValue, Condition.AndAlso((LambdaExpression)new MethodReplacer(MutatorsHelperFunctions.EachMethod, MutatorsHelperFunctions.CurrentMethod).Visit(condition.MergeFrom2Roots(PathToSourceChild, PathToChild))));
        }

        public ConverterConfigurator<TSourceRoot, TDestRoot> ToRoot()
        {
            return new ConverterConfigurator<TSourceRoot, TDestRoot>(root, Condition);
        }

        public Expression<Func<TSourceRoot, TSourceChild>> PathToSourceChild { get; }
        public Expression<Func<TDestRoot, TDestChild>> PathToChild { get; }
        public Expression<Func<TDestRoot, TDestValue>> PathToValue { get; }
        public LambdaExpression Condition { get; }
        private readonly ModelConfigurationNode root;
    }
}