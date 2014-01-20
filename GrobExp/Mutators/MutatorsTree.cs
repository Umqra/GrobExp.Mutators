﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using GrobExp.Mutators.Aggregators;
using GrobExp.Mutators.AutoEvaluators;
using GrobExp.Mutators.Visitors;

namespace GrobExp.Mutators
{
    public abstract class MutatorsTree<TData>
    {
        public KeyValuePair<Expression, List<KeyValuePair<int, MutatorConfiguration>>> GetRawMutators<TValue>(Expression<Func<TData, TValue>> path)
        {
            var nodeInfo = GetOrCreateNodeInfo(path);
            if(nodeInfo.RawMutators == null)
            {
                lock(lockObject)
                    if(nodeInfo.RawMutators == null)
                        nodeInfo.RawMutators = BuildRawMutators(path);
            }
            return nodeInfo.RawMutators.Value;
        }

        public KeyValuePair<Expression, List<MutatorConfiguration>> GetMutators<TValue>(Expression<Func<TData, TValue>> path)
        {
            var nodeInfo = GetOrCreateNodeInfo(path);
            if(nodeInfo.Mutators == null)
            {
                lock(lockObject)
                    if(nodeInfo.Mutators == null)
                        nodeInfo.Mutators = BuildMutators(path);
            }
            return nodeInfo.Mutators.Value;
        }

        public Func<TData, ValidationResultTreeNode> GetValidator()
        {
            return GetValidator(data => data);
        }

        public Func<TChild, ValidationResultTreeNode> GetValidator<TChild>(Expression<Func<TData, TChild>> path)
        {
            var validator = GetValidatorInternal(path);
            return child =>
                {
                    var tree = new ValidationResultTreeNode();
                    validator(child, tree);
                    return tree;
                };
        }

        public Func<TValue, bool> GetStaticValidator<TValue>(Expression<Func<TData, TValue>> path)
        {
            var nodeInfo = GetOrCreateNodeInfo(path);
            if(nodeInfo.StaticValidator == null)
            {
                lock(lockObject)
                    if(nodeInfo.StaticValidator == null)
                        nodeInfo.StaticValidator = BuildStaticValidator(path);
            }
            return nodeInfo.StaticValidator;
        }

        public Action<TData> GetTreeMutator()
        {
            return GetTreeMutator(data => data);
        }

        public Action<TChild> GetTreeMutator<TChild>(Expression<Func<TData, TChild>> path)
        {
            var nodeInfo = GetOrCreateNodeInfo(path);
            if(nodeInfo.TreeMutator == null)
            {
                lock(lockObject)
                    if(nodeInfo.TreeMutator == null)
                        nodeInfo.TreeMutator = BuildTreeMutator(path);
            }
            return nodeInfo.TreeMutator;
        }

        public abstract MutatorsTree<T> Migrate<T>(ModelConfigurationNode converterTree);
        public abstract MutatorsTree<TData> Merge(MutatorsTree<TData> other);

        internal Action<TChild, ValidationResultTreeNode> GetValidatorInternal<TChild>(Expression<Func<TData, TChild>> path)
        {
            var nodeInfo = GetOrCreateNodeInfo(path);
            if(nodeInfo.Validator == null)
            {
                lock(lockObject)
                    if(nodeInfo.Validator == null)
                        nodeInfo.Validator = BuildValidator(path);
            }
            return nodeInfo.Validator;
        }

        protected List<MutatorConfiguration> Canonize(IEnumerable<KeyValuePair<int, MutatorConfiguration>> mutators)
        {
            // todo ich: kill, as soon as scripts are fixed
            // todo ich: handle validation priorities
            if(mutators == null) return null;
            var hideIfConfigurations = new List<HideIfConfiguration>();
            var disableIfConfigurations = new List<DisableIfConfiguration>();
            var staticAggregatorConfigurations = new Dictionary<string, List<ConditionalAggregatorConfiguration>>();
            //var validatorConfigurations = new List<KeyValuePair<int, ValidatorConfiguration>>();
            SetSourceArrayConfiguration setSourceArrayConfiguration = null;
            var otherConfigurations = new List<MutatorConfiguration>();
            foreach(var mutator in mutators)
            {
                if(mutator.Value is HideIfConfiguration)
                    hideIfConfigurations.Add((HideIfConfiguration)mutator.Value);
                else if(mutator.Value is DisableIfConfiguration)
                    disableIfConfigurations.Add((DisableIfConfiguration)mutator.Value);
//                else if(mutator.Value is ValidatorConfiguration)
//                    validatorConfigurations.Add(new KeyValuePair<int, ValidatorConfiguration>(mutator.Key, (ValidatorConfiguration)mutator.Value));
                else if(mutator.Value is SetSourceArrayConfiguration)
                {
                    var currentSetSourceArrayConfiguration = (SetSourceArrayConfiguration)mutator.Value;
                    if(setSourceArrayConfiguration == null)
                        setSourceArrayConfiguration = currentSetSourceArrayConfiguration;
                    else if(!ExpressionEquivalenceChecker.Equivalent(setSourceArrayConfiguration.SourceArray, currentSetSourceArrayConfiguration.SourceArray, false, true))
                        throw new InvalidOperationException("An attempt to set array from different sources: '" + setSourceArrayConfiguration.SourceArray + "' and '" + currentSetSourceArrayConfiguration.SourceArray + "'");
                }
                else if(mutator.Value is ConditionalAggregatorConfiguration)
                {
                    var staticAggregator = (ConditionalAggregatorConfiguration)mutator.Value;
                    List<ConditionalAggregatorConfiguration> staticAggregators;
                    if(!staticAggregatorConfigurations.TryGetValue(staticAggregator.Name, out staticAggregators))
                        staticAggregatorConfigurations.Add(staticAggregator.Name, staticAggregators = new List<ConditionalAggregatorConfiguration>());
                    staticAggregators.Add(staticAggregator);
                }
                else
                    otherConfigurations.Add(mutator.Value);
            }
            if(hideIfConfigurations.Count == 1)
                otherConfigurations.Add(hideIfConfigurations.Single());
            else if(hideIfConfigurations.Count > 1)
            {
                var condition = hideIfConfigurations[0].Condition;
                var type = hideIfConfigurations[0].Type;
                for(var i = 1; i < hideIfConfigurations.Count; ++i)
                    condition = condition.OrElse(hideIfConfigurations[i].Condition);
                otherConfigurations.Add(new HideIfConfiguration(type, condition));
            }
            if(disableIfConfigurations.Count == 1)
                otherConfigurations.Add(disableIfConfigurations.Single());
            else if(disableIfConfigurations.Count > 1)
            {
                var condition = disableIfConfigurations[0].Condition;
                var type = disableIfConfigurations[0].Type;
                for(var i = 1; i < disableIfConfigurations.Count; ++i)
                    condition = condition.OrElse(disableIfConfigurations[i].Condition);
                otherConfigurations.Add(new DisableIfConfiguration(type, condition));
            }
            foreach(var item in staticAggregatorConfigurations)
            {
                var staticAggregators = item.Value;
                if(staticAggregators.Count == 1)
                    otherConfigurations.Add(staticAggregators.Single());
                else
                {
                    var condition = staticAggregators[0].Condition;
                    var type = staticAggregators[0].Type;
                    for(var i = 1; i < staticAggregators.Count; ++i)
                        condition = condition.OrElse(staticAggregators[i].Condition);
                    otherConfigurations.Add(new ConditionalAggregatorConfiguration(type, condition, item.Key));
                }
            }
            if(setSourceArrayConfiguration != null)
                otherConfigurations.Add(setSourceArrayConfiguration);
            return otherConfigurations;
        }

        protected abstract KeyValuePair<Expression, List<KeyValuePair<int, MutatorConfiguration>>> BuildRawMutators<TValue>(Expression<Func<TData, TValue>> path);
        protected abstract KeyValuePair<Expression, List<MutatorConfiguration>> BuildMutators<TValue>(Expression<Func<TData, TValue>> path);
        protected abstract Action<TChild, ValidationResultTreeNode> BuildValidator<TChild>(Expression<Func<TData, TChild>> path);
        protected abstract Func<TValue, bool> BuildStaticValidator<TValue>(Expression<Func<TData, TValue>> path);
        protected abstract Action<TChild> BuildTreeMutator<TChild>(Expression<Func<TData, TChild>> path);

        private NodeInfo<T> GetOrCreateNodeInfo<T>(Expression<Func<TData, T>> path)
        {
            var key = new ExpressionWrapper(path.Body, false);
            var nodeInfo = (NodeInfo<T>)hashtable[key];
            if(nodeInfo == null)
            {
                lock(lockObject)
                {
                    nodeInfo = (NodeInfo<T>)hashtable[key];
                    if(nodeInfo == null)
                        hashtable[key] = nodeInfo = new NodeInfo<T>();
                }
            }
            return nodeInfo;
        }

        private readonly Hashtable hashtable = new Hashtable();
        private readonly object lockObject = new object();

        private class NodeInfo<T>
        {
            public KeyValuePair<Expression, List<KeyValuePair<int, MutatorConfiguration>>>? RawMutators { get; set; }
            public KeyValuePair<Expression, List<MutatorConfiguration>>? Mutators { get; set; }
            public Action<T, ValidationResultTreeNode> Validator { get; set; }
            public Func<T, bool> StaticValidator { get; set; }
            public Action<T> TreeMutator { get; set; }
        }
    }
}