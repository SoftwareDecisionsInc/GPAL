// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Reads and writes an array of two or more dimensions, such as int[,], as rows of rows: new int[,] { { 1, 2 }, { 3, 4 } }
/// is [[1, 2], [3, 4]]. YamlDotNet on its own writes its values in one list and cannot read them back.
/// </summary>
public class MultiDimensionalArrayYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type.IsArray && 1 < type.GetArrayRank();
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // read as the same array one dimension at a time, int[][] for int[,]; a null stays null
        object rows = rootDeserializer(GenerallyPositive.ConverterHelper.RowsTypeOf(type));

        return null == rows ? null : GenerallyPositive.ConverterHelper.ArrayFromRows(rows, type, null);
    }

    // The grids being written on this thread. A grid met again inside itself, as one holding its own owner, is written as
    // the text Recursion to [type], as the other writers write a link back, where writing it again never ends
    [ThreadStatic]
    private static HashSet<object> gridsBeingWritten;

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (null == gridsBeingWritten)
            gridsBeingWritten = new HashSet<object>(GenerallyPositive.ConverterHelper.ReferenceEqualityComparer.Instance);

        if (value is Array linkedGrid && true == gridsBeingWritten.Contains(linkedGrid))
            emitter.Emit(new Scalar(null, null, $"Recursion to [{linkedGrid.GetType().Name}]"));
        else if (value is Array grid)
        {
            gridsBeingWritten.Add(grid);
            try
            {
                int[] at = new int[grid.Rank];
                void Rows(int dimension)
                {
                    emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Any));
                    for (int index = 0; index < grid.GetLength(dimension); index++)
                    {
                        at[dimension] = grid.GetLowerBound(dimension) + index;
                        object item = dimension + 1 < grid.Rank ? null : grid.GetValue(at);
                        if (dimension + 1 < grid.Rank)
                            Rows(dimension + 1);
                        // YamlDotNet throws serializing a null it is handed, so a null is written here
                        else if (null == item)
                            emitter.Emit(new Scalar("null"));
                        else
                            serializer(item, item.GetType());
                    }
                    emitter.Emit(new SequenceEnd());
                }
                Rows(0);
            }
            finally
            {
                gridsBeingWritten.Remove(grid);
            }
        }
        else
            emitter.Emit(new Scalar("null"));   // a null-valued scalar renders as '' which does not read back as null
    }
}
