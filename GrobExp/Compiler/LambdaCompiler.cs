﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using GrEmit;

namespace GrobExp.Compiler
{
    public static class LambdaCompiler
    {
        public static Delegate Compile(LambdaExpression lambda, CompilerOptions options)
        {
            CompiledLambda[] subLambdas;
            var debugInfoGenerator = string.IsNullOrEmpty(DebugOutputDirectory) ? null : DebugInfoGenerator.CreatePdbGenerator();
            return CompileInternal(lambda, debugInfoGenerator, out subLambdas, options).Delegate;
        }

        public static TDelegate Compile<TDelegate>(Expression<TDelegate> lambda, CompilerOptions options) where TDelegate : class
        {
            CompiledLambda[] subLambdas;
            var debugInfoGenerator = string.IsNullOrEmpty(DebugOutputDirectory) ? null : DebugInfoGenerator.CreatePdbGenerator();
            return (TDelegate)(object)CompileInternal(lambda, debugInfoGenerator, out subLambdas, options).Delegate;
        }

        public static void CompileToMethod(LambdaExpression lambda, MethodBuilder method, CompilerOptions options)
        {
            CompileToMethodInternal(lambda, method, null, options);
        }

        public static void CompileToMethod(LambdaExpression lambda, MethodBuilder method, DebugInfoGenerator debugInfoGenerator, CompilerOptions options)
        {
            CompileToMethodInternal(lambda, method, debugInfoGenerator, options);
        }

        public static bool AnalyzeILStack = true;
        public static string DebugOutputDirectory = null;
        public static double TotalJITCompilationTime = 0;

        internal static CompiledLambda CompileInternal(
            LambdaExpression lambda,
            DebugInfoGenerator debugInfoGenerator,
            Type closureType,
            ParameterExpression closureParameter,
            Type constantsType,
            ParameterExpression constantsParameter,
            object constants,
            Dictionary<SwitchExpression, Tuple<FieldInfo, FieldInfo, int>> switches,
            CompilerOptions options,
            List<CompiledLambda> compiledLambdas)
        {
            if(debugInfoGenerator == null)
                return CompileToDynamicMethod(lambda, closureType, closureParameter, constantsType, constantsParameter, constants, switches, options, compiledLambdas);

            var parameters = lambda.Parameters.ToArray();
            var parameterTypes = parameters.Select(parameter => parameter.Type).ToArray();
            var returnType = lambda.ReturnType;

            var typeBuilder = Module.DefineType(Guid.NewGuid().ToString(), TypeAttributes.Public | TypeAttributes.Class);
            var method = typeBuilder.DefineMethod(lambda.Name ?? Guid.NewGuid().ToString(), MethodAttributes.Static | MethodAttributes.Public, returnType, parameterTypes);
            for(var i = 0; i < parameters.Length; ++i)
                method.DefineParameter(i + 1, ParameterAttributes.None, parameters[i].Name);
            CompileToMethodInternal(lambda, debugInfoGenerator, closureType, closureParameter, constantsType, constantsParameter, switches, options, compiledLambdas, method);

            var type = typeBuilder.CreateType();
            var dynamicMethod = new DynamicMethod(Guid.NewGuid().ToString(), returnType, parameterTypes, Module, true);
            using(var il = new GroboIL(dynamicMethod))
            {
                for(var i = 0; i < parameterTypes.Length; ++i)
                    il.Ldarg(i);
                il.Call(type.GetMethod(method.Name));
                il.Ret();
            }
            return new CompiledLambda
                {
                    Delegate = dynamicMethod.CreateDelegate(Extensions.GetDelegateType(constantsParameter == null ? parameterTypes : parameterTypes.Skip(1).ToArray(), returnType), constants),
                    Method = method
                };
        }

        internal static void CompileToMethodInternal(
            LambdaExpression lambda,
            DebugInfoGenerator debugInfoGenerator,
            Type closureType,
            ParameterExpression closureParameter,
            Type constantsType,
            ParameterExpression constantsParameter,
            Dictionary<SwitchExpression, Tuple<FieldInfo, FieldInfo, int>> switches,
            CompilerOptions options,
            List<CompiledLambda> compiledLambdas,
            MethodBuilder method)
        {
            var typeBuilder = method.ReflectedType as TypeBuilder;
            if(typeBuilder == null)
                throw new ArgumentException("Unable to obtain type builder of the method", "method");
            using(var il = new GroboIL(method, AnalyzeILStack))
            {
                var context = new EmittingContext
                    {
                        Options = options,
                        DebugInfoGenerator = debugInfoGenerator,
                        TypeBuilder = typeBuilder,
                        Lambda = lambda,
                        Method = method,
                        SkipVisibility = false,
                        Parameters = lambda.Parameters.ToArray(),
                        ClosureType = closureType,
                        ClosureParameter = closureParameter,
                        ConstantsType = constantsType,
                        ConstantsParameter = constantsParameter,
                        Switches = switches,
                        CompiledLambdas = compiledLambdas,
                        Il = il
                    };
                CompileInternal(lambda, context);
            }
        }

        internal static readonly AssemblyBuilder Assembly = CreateAssembly();
        internal static readonly ModuleBuilder Module = Assembly.DefineDynamicModule(Guid.NewGuid().ToString(), true);

        private static string GenerateFileName(Expression expression)
        {
            var hash = ExpressionHashCalculator.CalcHashCode(expression, true);
            if(!Directory.Exists(DebugOutputDirectory))
                Directory.CreateDirectory(DebugOutputDirectory);
            return Path.Combine(DebugOutputDirectory, "Z" + Math.Abs(hash) + ".lambda");
        }

        private static void CompileInternal(LambdaExpression lambda, EmittingContext context)
        {
            var returnType = lambda.ReturnType;
            var il = context.Il;
            var returnDefaultValueLabel = context.CanReturn ? il.DefineLabel("returnDefaultValue") : null;
            Type resultType;
            var whatReturn = returnType == typeof(void) ? ResultType.Void : ResultType.Value;
            var labelUsed = ExpressionEmittersCollection.Emit(lambda.Body, context, returnDefaultValueLabel, whatReturn, false, out resultType);
            if(returnType == typeof(bool) && resultType == typeof(bool?))
                context.ConvertFromNullableBoolToBool();
            if(returnType == typeof(void) && resultType != typeof(void))
            {
                using(var temp = context.DeclareLocal(resultType))
                    il.Stloc(temp);
            }
            il.Ret();
            if(!labelUsed)
                return;
            context.MarkLabelAndSurroundWithSP(returnDefaultValueLabel);
            il.Pop();
            if(returnType != typeof(void))
            {
                if(!returnType.IsValueType)
                    il.Ldnull();
                else
                {
                    using(var defaultValue = context.DeclareLocal(returnType))
                    {
                        il.Ldloca(defaultValue);
                        il.Initobj(returnType);
                        il.Ldloc(defaultValue);
                    }
                }
            }
            il.Ret();
        }

        private static CompiledLambda CompileToDynamicMethod(
            LambdaExpression lambda,
            Type closureType,
            ParameterExpression closureParameter,
            Type constantsType,
            ParameterExpression constantsParameter,
            object constants,
            Dictionary<SwitchExpression, Tuple<FieldInfo, FieldInfo, int>> switches,
            CompilerOptions options,
            List<CompiledLambda> compiledLambdas)
        {
            var parameters = lambda.Parameters.ToArray();
            var parameterTypes = parameters.Select(parameter => parameter.Type).ToArray();
            var returnType = lambda.ReturnType;
            var method = new DynamicMethod(lambda.Name ?? Guid.NewGuid().ToString(), MethodAttributes.Static | MethodAttributes.Public, CallingConventions.Standard, returnType, parameterTypes, Module, true);
            using(var il = new GroboIL(method, AnalyzeILStack))
            {
                var context = new EmittingContext
                    {
                        Options = options,
                        Lambda = lambda,
                        Method = method,
                        SkipVisibility = true,
                        Parameters = parameters,
                        ClosureType = closureType,
                        ClosureParameter = closureParameter,
                        ConstantsType = constantsType,
                        ConstantsParameter = constantsParameter,
                        Switches = switches,
                        CompiledLambdas = compiledLambdas,
                        Il = il
                    };
                CompileInternal(lambda, context);
            }
            var compiledMethod = method.CreateDelegate(Extensions.GetDelegateType(constantsParameter == null ? parameterTypes : parameterTypes.Skip(1).ToArray(), returnType), constants);
            var stopwatch = Stopwatch.StartNew();
            DynamicMethodCleaner(method);
            TotalJITCompilationTime += stopwatch.Elapsed.TotalSeconds;
            return new CompiledLambda
            {
                Delegate = compiledMethod,
                Method = method
            };
        }

        public static readonly Action<DynamicMethod> DynamicMethodCleaner = BuildDynamicMethodCleaner();

        private static Action<DynamicMethod> BuildDynamicMethodCleaner()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(void), new[] {typeof(DynamicMethod)}, typeof(string), true);
            var getMethodDescriptorMethod = typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.Instance | BindingFlags.NonPublic);
            if (getMethodDescriptorMethod == null)
                throw new MissingMethodException(typeof(DynamicMethod).Name, "GetMethodDescriptor");
            var prepareMethodMethod = typeof(RuntimeHelpers).GetMethod("PrepareMethod", new[] { typeof(RuntimeMethodHandle) });
            if (prepareMethodMethod == null)
                throw new MissingMethodException(typeof(RuntimeHelpers).Name, "PrepareMethod");
            var ilGeneratorField = typeof(DynamicMethod).GetField("m_ilGenerator", BindingFlags.Instance | BindingFlags.NonPublic);
            if(ilGeneratorField == null)
                throw new InvalidOperationException("The field 'DynamicMethod.m_ilGenerator' is not found");
            var dynamicILInfoField = typeof(DynamicMethod).GetField("m_DynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (dynamicILInfoField == null)
                throw new InvalidOperationException("The field 'DynamicMethod.m_DynamicILInfo' is not found");
            var resolverField = typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic);
            if (resolverField == null)
                throw new InvalidOperationException("The field 'DynamicMethod.m_resolver' is not found");
            using(var il = new GroboIL(method))
            {
                il.Ldarg(0);
                il.Call(getMethodDescriptorMethod);
                il.Call(prepareMethodMethod);
                il.Ldarg(0);
                il.Ldnull();
                il.Stfld(ilGeneratorField);
                il.Ldarg(0);
                il.Ldnull();
                il.Stfld(dynamicILInfoField);
                il.Ldarg(0);
                il.Ldnull();
                il.Stfld(resolverField);
                il.Ret();
            }
            return (Action<DynamicMethod>)method.CreateDelegate(typeof(Action<DynamicMethod>));
        }

        private static AssemblyBuilder CreateAssembly()
        {
            var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run);

//            Type daType = typeof(AssemblyFlagsAttribute);
//            ConstructorInfo daCtor = daType.GetConstructor(new[] {typeof(AssemblyNameFlags)});
////[assembly : AssemblyFlags(AssemblyNameFlags.EnableJITcompileOptimizer)]
//            var daBuilder = new CustomAttributeBuilder(daCtor, new object[]
//                {
//                    AssemblyNameFlags.EnableJITcompileOptimizer
//                });
//            assemblyBuilder.SetCustomAttribute(daBuilder);
            return assemblyBuilder;
        }

        private static CompiledLambda CompileInternal(LambdaExpression lambda, DebugInfoGenerator debugInfoGenerator, out CompiledLambda[] subLambdas, CompilerOptions options)
        {
            var compiledLambdas = new List<CompiledLambda>();
            Type closureType;
            ParameterExpression closureParameter;
            Type constantsType;
            ParameterExpression constantsParameter;
            object constants;
            Dictionary<SwitchExpression, Tuple<FieldInfo, FieldInfo, int>> switches;
            var emitToDynamicMethod = debugInfoGenerator == null;
            var resolvedLambda = new ExpressionClosureResolver(lambda, Module, emitToDynamicMethod).Resolve(out closureType, out closureParameter, out constantsType, out constantsParameter, out constants, out switches);
            if(!string.IsNullOrEmpty(DebugOutputDirectory))
            {
                resolvedLambda = AdvancedDebugViewWriter.WriteToModifying(resolvedLambda, constantsType,
                                                                          constantsParameter, constants, GenerateFileName(resolvedLambda));
            }
            var compiledLambda = CompileInternal(resolvedLambda, debugInfoGenerator, closureType, closureParameter, constantsType, constantsParameter, constants, switches, options, compiledLambdas);
            subLambdas = compiledLambdas.ToArray();
            if(compiledLambdas.Count > 0 && emitToDynamicMethod)
                BuildDelegatesFoister(constantsType)(constants, compiledLambdas.Select(compIledLambda => compIledLambda.Delegate).ToArray());
            return compiledLambda;
        }

        private static void CompileToMethodInternal(LambdaExpression lambda, MethodBuilder method, DebugInfoGenerator debugInfoGenerator, CompilerOptions options)
        {
            var compiledLambdas = new List<CompiledLambda>();
            Type closureType;
            ParameterExpression closureParameter;
            Type constantsType;
            ParameterExpression constantsParameter;
            object constants;
            Dictionary<SwitchExpression, Tuple<FieldInfo, FieldInfo, int>> switches;
            var module = method.Module as ModuleBuilder;
            if(module == null)
                throw new ArgumentException("Unable to obtain module builder of the method", "method");
            method.SetReturnType(lambda.ReturnType);
            method.SetParameters(lambda.Parameters.Select(parameter => parameter.Type).ToArray());
            var resolvedLambda = new ExpressionClosureResolver(lambda, module, false).Resolve(out closureType, out closureParameter, out constantsType, out constantsParameter, out constants, out switches);
            if(!string.IsNullOrEmpty(DebugOutputDirectory))
            {
                resolvedLambda = AdvancedDebugViewWriter.WriteToModifying(resolvedLambda, constantsType,
                                                                          constantsParameter, constants, GenerateFileName(resolvedLambda));
            }
            if(constantsParameter != null)
                throw new InvalidOperationException("Non-trivial constants are not allowed for compilation to method");
            CompileToMethodInternal(resolvedLambda, debugInfoGenerator, closureType, closureParameter, null, null, switches, options, compiledLambdas, method);
        }

        private static Action<object, Delegate[]> BuildDelegatesFoister(Type type)
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Standard, typeof(void), new[] {typeof(object), typeof(Delegate[])}, Module, true);
            using(var il = new GroboIL(method))
            {
                il.Ldarg(0);
                il.Castclass(type);
                il.Ldarg(1);
                il.Stfld(type.GetField("delegates"));
                il.Ret();
            }
            return (Action<object, Delegate[]>)method.CreateDelegate(typeof(Action<object, Delegate[]>));
        }
    }
}