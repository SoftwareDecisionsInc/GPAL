// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Spreadsheet;
using HtmlAgilityPack;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using YamlDotNet.Core;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    partial class ConverterHelper
    {
        // For correct reference equality in HashSet<object>
        public class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
        /// <summary>
        /// Walks an object graph once to count nodes and detect a cycle, stopping at <paramref name="limit"/> nodes or
        /// the first cycle.
        /// </summary>
        /// <remarks>
        /// Stops at limit nodes or the first cycle, so cost does not grow with the payload. Cycles are judged by
        /// reference identity, not Equals.
        /// </remarks>
        /// <param name="root">The graph to walk</param>
        /// <param name="limit">Node count to stop at. Zero or less walks the whole graph and only checks for a cycle</param>
        /// <param name="cyclic">Set to true when a node appears twice on one path</param>
        /// <returns>Number of nodes counted, never more than <paramref name="limit"/></returns>
        public static int CountGraphNodes(object root, int limit, out bool cyclic)
        {
            int count = 0;
            bool foundCycle = false;

            CountGraphNodesRec(root, limit, ref count, ref foundCycle, new HashSet<object>(ReferenceComparer.Instance));

            cyclic = foundCycle;

            return count;
        }

        /// <summary>
        /// Counts nodes in an object graph, stopping at <paramref name="limit"/>.
        /// </summary>
        /// <param name="root">The graph to walk</param>
        /// <param name="limit">Node count to stop at</param>
        /// <returns>Nodes counted</returns>
        public static int CountGraphNodes(object root, int limit)
        {
            return CountGraphNodes(root, limit, out bool _);
        }

        private static void CountGraphNodesRec(object node, int limit, ref int count, ref bool cyclic, HashSet<object> onPath)
        {
            if (true == cyclic || null == node || (0 < limit && count >= limit))
                return;

            Type nodeType = node.GetType();

            // Only reference types can form a cycle and strings may be interned. A struct is its own boxed copy, which no
            // other node matches, so it is on the path only to count how deep the walk is, as a struct whose property
            // returns a new one on each read goes on without end.
            bool tracked = false == (node is string);

            if (true == tracked && false == onPath.Add(node))
            {
                cyclic = true;
                return;
            }

            count++;

            // a node NestingLimit deep is not walked further, as a class whose property returns a new one on each read
            // goes on without end
            if ((0 >= limit || count < limit) && NestingLimit > onPath.Count)
            {
                if (node is IDictionary dictionary)
                {
                    foreach (object value in dictionary.Values)
                    {
                        CountGraphNodesRec(value, limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
                else if (node is IEnumerable enumerable)
                {
                    foreach (object value in enumerable)
                    {
                        CountGraphNodesRec(value, limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
                // a class of the framework, such as a FileInfo, is one node: its members can lead on without end, as a
                // DirectoryInfo's Root does
                else if (false == IsFrameworkObjectType(nodeType))
                {
                    foreach (MemberInfo member in CyclableMembers(nodeType))
                    {
                        CountGraphNodesRec(MemberValue(member, node), limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
            }

            // Remove the node from the path when leaving it. A node reached from separate branches is shared, not a
            // cycle.
            if (true == tracked)
                onPath.Remove(node);
        }

        /// <summary>
        /// Returns a type's readable properties and fields that can lead to a cycle. Cached per type.
        /// </summary>
        /// <param name="type">Type to inspect</param>
        /// <returns>The members to walk; empty for a type that cannot be part of a cycle</returns>
        internal static MemberInfo[] CyclableMembers(Type type)
        {
            return graphMembers.GetOrAdd(type, describing =>
            {
                List<MemberInfo> worthWalking = new List<MemberInfo>();

                foreach (PropertyInfo property in describing.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (true == property.CanRead && 0 == property.GetIndexParameters().Length && true == CanTakePartInACycle(property.PropertyType))
                        worthWalking.Add(property);

                foreach (FieldInfo field in describing.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (true == CanTakePartInACycle(field.FieldType))
                        worthWalking.Add(field);

                return worthWalking.ToArray();
            });
        }

        /// <summary>
        /// Returns true if a value of this type can be part of a cycle, so large flat collections are not walked.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>False for a type that cannot be part of a cycle</returns>
        internal static bool CanTakePartInACycle(Type type)
        {
            return false == (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(decimal) || type == typeof(Guid)
                || type == typeof(TimeSpan) || type == typeof(Uri));
        }

        /// <summary>
        /// Reads one member's value; null if the getter throws.
        /// </summary>
        /// <param name="member">Property or field to read</param>
        /// <param name="node">Instance to read it from</param>
        /// <returns>The value, or null when it could not be read</returns>
        internal static object MemberValue(MemberInfo member, object node)
        {
            object retVal = null;

            try
            {
                retVal = member is PropertyInfo property ? property.GetValue(node) : ((FieldInfo)member).GetValue(node);
            }
            catch (Exception)
            {
                // a getter that throws is the type's business, not the walk's
            }

            return retVal;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MemberInfo[]> graphMembers
            = new System.Collections.Concurrent.ConcurrentDictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Reference-identity comparer for the set that tracks the current path.
        /// </summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object of)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(of);
            }
        }
    }
}
