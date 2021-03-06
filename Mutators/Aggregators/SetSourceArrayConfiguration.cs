﻿using System;
using System.Linq;
using System.Linq.Expressions;

using GrobExp.Mutators.Visitors;

namespace GrobExp.Mutators.Aggregators
{
    public class SetSourceArrayConfiguration : AggregatorConfiguration
    {
        protected SetSourceArrayConfiguration(Type type, LambdaExpression sourceArray)
            : base(type)
        {
            SourceArray = sourceArray;
        }

        public static SetSourceArrayConfiguration Create(LambdaExpression sourceArray)
        {
            return new SetSourceArrayConfiguration(sourceArray.Parameters.Single().Type, Prepare(sourceArray));
        }

        public override MutatorConfiguration ToRoot(LambdaExpression path)
        {
            return new SetSourceArrayConfiguration(path.Parameters.Single().Type, path.Merge(SourceArray));
        }

        public override MutatorConfiguration Mutate(Type to, Expression path, CompositionPerformer performer)
        {
            return new SetSourceArrayConfiguration(to, Resolve(path, performer, SourceArray));
        }

        public override MutatorConfiguration ResolveAliases(LambdaAliasesResolver resolver)
        {
            return new SetSourceArrayConfiguration(Type, resolver.Resolve(SourceArray));
        }

        public override MutatorConfiguration If(LambdaExpression condition)
        {
            throw new NotSupportedException();
        }

        public override void GetArrays(ArraysExtractor arraysExtractor)
        {
            arraysExtractor.GetArrays(SourceArray);
        }

        public LambdaExpression SourceArray { get; set; }

        protected override LambdaExpression[] GetDependencies()
        {
            return new LambdaExpression[0];
        }
    }
}