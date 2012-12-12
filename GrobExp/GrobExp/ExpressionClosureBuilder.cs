﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GrobExp
{
    public class ExpressionClosureBuilder : ExpressionVisitor
    {
        public ExpressionClosureBuilder(LambdaExpression lambda)
        {
            this.lambda = lambda;
            string name = "Closure_" + (uint)Interlocked.Increment(ref closureId);
            typeBuilder = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class, typeof(Closure));
        }

        public Type Build(out Dictionary<ConstantExpression, FieldInfo> constants, out Dictionary<ParameterExpression, FieldInfo> parameters)
        {
            Visit(lambda);
            Action initializer = BuildInitializer();
            Type result = typeBuilder.CreateType();
            initializer();
            constants = this.constants.ToDictionary(item => item.Key, item => result.GetField(item.Value.Name));
            parameters = this.parameters.ToDictionary(item => item.Key, item => result.GetField(item.Value.Name));
            return result;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            localParameters.Push(new HashSet<ParameterExpression>(node.Parameters));
            var res = base.VisitLambda(node);
            localParameters.Pop();
            return res;
        }

        protected override Expression VisitBlock(BlockExpression node)
        {
            var peek = localParameters.Peek();
            foreach(var variable in node.Variables)
                peek.Add(variable);
            var res = base.VisitBlock(node);
            foreach(var variable in node.Variables)
                peek.Remove(variable);
            return res;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if(node.Value == null || node.Type.IsPrimitive || node.Type == typeof(string))
                return node;
            var key = new KeyValuePair<Type, object>(node.Type, node.Value);
            var field = (FieldInfo)hashtable[key];
            if(field == null)
            {
                field = typeBuilder.DefineField(GetFieldName(node.Type), GetFieldType(node.Type), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
                hashtable[key] = field;
            }
            if(!constants.ContainsKey(node))
                constants.Add(node, field);
            return node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            var peek = localParameters.Peek();
            if(!peek.Contains(node) && !parameters.ContainsKey(node))
            {
                FieldInfo field = typeBuilder.DefineField(GetFieldName(node.Type), GetFieldType(node.Type), FieldAttributes.Public);
                parameters.Add(node, field);
            }
            return base.VisitParameter(node);
        }

        private Action BuildInitializer()
        {
            var method = typeBuilder.DefineMethod("Initialize", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] {typeof(object[])});
            var il = new GrobIL(method.GetILGenerator(), false, typeof(void), new[] {typeof(object[])});
            var consts = new object[hashtable.Count];
            int index = 0;
            foreach(DictionaryEntry entry in hashtable)
            {
                var pair = (KeyValuePair<Type, object>)entry.Key;
                var type = pair.Key;
                consts[index] = pair.Value;
                il.Ldnull();
                il.Ldarg(0);
                il.Ldc_I4(index++);
                il.Ldelem(typeof(object));
                var field = (FieldInfo)entry.Value;
                if (type.IsValueType)
                {
                    il.Unbox_Any(type);
                    if(field.FieldType != type)
                    {
                        var constructor = field.FieldType.GetConstructor(new[] {type});
                        if(constructor == null)
                            throw new InvalidOperationException("Missing constructor of type '" + Format(field.FieldType) + "' with parameter of type '" + Format(type) + "'");
                        il.Newobj(constructor);
                    }
                }
                else if(field.FieldType != type)
                    throw new InvalidOperationException("Attempt to assign a value of type '" + Format(type) + "' to field of type '" + Format(field.FieldType) + "'");
                il.Stfld(field);
            }
            il.Ret();
            return () => typeBuilder.GetMethod("Initialize").Invoke(null, new[] {consts});
        }

        private static Type GetFieldType(Type type)
        {
            return (type.IsNestedPrivate || type.IsNotPublic) && type.IsValueType
                       ? typeof(StrongBox<>).MakeGenericType(new[] {type})
                       : type;
        }

        private static string Format(Type type)
        {
            if(!type.IsGenericType)
                return type.Name;
            return type.Name + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
        }

        private string GetFieldName(Type type)
        {
            return Format(type) + "_" + fieldId++;
        }

        private static int closureId;
        private int fieldId;

        private readonly LambdaExpression lambda;
        private readonly Stack<HashSet<ParameterExpression>> localParameters = new Stack<HashSet<ParameterExpression>>();

        private readonly Hashtable hashtable = new Hashtable();
        private readonly Dictionary<ConstantExpression, FieldInfo> constants = new Dictionary<ConstantExpression, FieldInfo>();
        private readonly Dictionary<ParameterExpression, FieldInfo> parameters = new Dictionary<ParameterExpression, FieldInfo>();

        private readonly TypeBuilder typeBuilder;

        private static readonly AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder module = assembly.DefineDynamicModule(Guid.NewGuid().ToString());
    }
}