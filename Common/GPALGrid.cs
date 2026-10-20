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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// A generic grid implementation for storing and manipulating tabular data in rows and columns.
    /// The grid uses a <see cref="List{T}"/> of <see cref="List{T}"/> to store data, where each inner list represents a row.
    /// Supports operations like adding/deleting rows, adding columns, and accessing cells.
    /// Errors are logged using <see cref="GPAL.PublishSimpleEvent"/> to allow graceful continuation without throwing exceptions.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the grid.</typeparam>
    /// <example>
    /// <code>
    /// var grid = new GPALGrid&lt;dynamic&gt;();
    /// 
    /// // Add rows using different methods
    /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
    /// grid.AddRow(new { Name = "Alice" });
    /// grid.AddRow("John");
    /// 
    /// // Print the grid
    /// foreach (var row in grid)
    /// {
    ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
    /// }
    /// // Output:
    /// // Mike me
    /// // Alice
    /// // John
    /// </code>
    /// </example>
    internal class GPALGrid<T> : IGPALGrid<T>
    {
        private List<List<T>> data;
        // GPAL.Workflow adds rows from several threads, so the count check and add share one lock.
        private readonly object rowLock = new object();

        /// <summary>
        /// When true, GetGrid fingerprints and skips duplicate rows (e.g. from infinite-scroll re-passes). Off by
        /// default because the per-element attribute hashing is expensive on slow engines; opt in with
        /// <see cref="WithDedupeData(bool)"/> on the grid you pass into GetGrid.
        /// </summary>
        internal bool UseDedupe { get; private set; } = false;

        /// <summary>
        /// Turns duplicate-row detection on (or off) for this grid, read by GetGrid when this grid is passed in.
        /// </summary>
        /// <param name="useDedupe">True to dedupe rows during GetGrid; false to keep every row.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        public IGPALGrid<T> WithDedupeData(bool useDedupe = true)
        {
            UseDedupe = useDedupe;
            return this;
        }

		/// <summary>
		/// Initializes a new instance of the <see cref="GPALGrid{T}"/> class with an empty grid.
		/// </summary>
		internal GPALGrid()
        {
            data = new List<List<T>>();
        }

        /// <summary>
        /// Adds a new row to the grid using a list of values.
        /// Validates the number of values against the grid's column count (if the grid has rows).
        /// If validation fails, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and adds the row as-is.
        /// </summary>
        /// <param name="row">The list of values to add as a row.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// grid.AddRow(new List&lt;dynamic&gt; { "Benny is great" });
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output:
        /// // Mike me
        /// // Benny is great
        /// </code>
        /// </example>
        public IAllowGridActions<T> AddRow(List<T> row)
        {
            if (row == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Row is null in AddRow(List<T>). Skipping addition.", this, GPALObjectType.Other);
                return this;
            }

            lock (rowLock)
                data.Add(row);

            return this;
        }
        /// <summary>
        /// Used internally to initialize a grid
        /// </summary>
        /// <example>
        /// <code>
        /// new GPALGrid&lt;string&gt;
        /// {
        ///     new List&lt;string&gt; { "Username", creds.Username },
        ///     new List&lt;string&gt; { "Password", creds.Password },
        /// };
        /// </code>
        /// </example>
        /// <param name="row">The row to add</param>
        internal void Add(List<T> row)
        {
            if (row == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Row is null in AddRow(List<T>). Skipping addition.", this, GPALObjectType.Other);
            }

            data.Add(row);
        }

        public IAllowGridActions<T> Add(IGPALGrid<T> grid)
        {
            foreach (List<T> row in grid)
                data.Add(row);

            return this;
        }
        /// <summary>
        /// Adds a new row to the grid using an anonymous type, where each property represents a column value.
        /// The properties are extracted in the order they are defined and converted to type <typeparamref name="T"/>.
        /// If the number of properties does not match the grid's column count (if the grid has rows), a warning is logged.
        /// If a property value cannot be converted to type <typeparamref name="T"/>, an error is logged and the default value for <typeparamref name="T"/> is used.
        /// Errors are logged using <see cref="GPAL.PublishSimpleEvent"/> to allow graceful continuation.
        /// </summary>
        /// <param name="anonymousObject">The anonymous object whose properties represent the row values.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// 
        /// // Add a row using an anonymous type
        /// grid.AddRow(new { Name = "Alice" });
        /// 
        /// // Add another row with multiple properties (if grid expects more columns)
        /// grid.AddRow(new { Name = "Bob", Age = 30 });
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output (depending on column count):
        /// // Alice
        /// // Bob, 30
        /// </code>
        /// </example>
        public IAllowGridActions<T> AddRow(object anonymousObject)
        {
            if (anonymousObject == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Anonymous object is null in AddRow(object). Skipping addition.", this, GPALObjectType.Other);
                return this;
            }

            // Convert anonymous object properties to a list of values
            var values = anonymousObject.GetType()
                .GetProperties()
                .Select(prop => prop.GetValue(anonymousObject, null))
                .ToList();

            // Validate column count if the grid already has rows
            lock (rowLock)
            {
                int expectedColumns = Columns;
                if (data.Count > 0 && values.Count != expectedColumns)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Anonymous object has [{values.Count}] values, but expected [{expectedColumns}]. Adding row as-is.", this, GPALObjectType.Other);
            }

            // Convert each value to type T
            var row = new List<T>();
            foreach (var value in values)
            {
                try
                {
                    if (value == null)
                    {
                        row.Add(default(T));
                        continue;
                    }
                    row.Add((T)Convert.ChangeType(value, typeof(T)));
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Cannot convert value [{value}] to type [{typeof(T).Name}]. Using default value", this, GPALObjectType.Other, ex);
                    row.Add(default(T));
                }
            }

            lock (rowLock)
                data.Add(row);

            return this;
        }

        /// <summary>
        /// Adds a new row to the grid using a variable number of arguments, where each argument represents a column value.
        /// If the number of arguments does not match the grid's column count (if the grid has rows), a warning is logged.
        /// The arguments must already be of type <typeparamref name="T"/>; no conversion is performed.
        /// Errors are logged using <see cref="GPAL.PublishSimpleEvent"/> to allow graceful continuation.
        /// </summary>
        /// <param name="values">The values to add as a row.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// 
        /// // Add a row using params
        /// grid.AddRow("John");
        /// 
        /// // Add another row with multiple values (if grid expects more columns)
        /// grid.AddRow("Jane", 25);
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output (depending on column count):
        /// // John
        /// // Jane, 25
        /// </code>
        /// </example>
        public IAllowGridActions<T> AddRow(params T[] values)
        {
            if (values == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Values array is null in AddRow(params T[]). Skipping addition.", this, GPALObjectType.Other);
                return this;
            }

            // Validate column count if the grid already has rows, and add while still holding the gate
            lock (rowLock)
            {
                int expectedColumns = Columns;
                if (data.Count > 0 && values.Length != expectedColumns)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Params array has [{values.Length}] values, but expected [{expectedColumns}]. Adding row as-is.", this, GPALObjectType.Other);

                data.Add(new List<T>(values));
            }
            return this;
        }

        /// <summary>
        /// Deletes a row at the specified index.
        /// If the index is invalid, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and skips the operation.
        /// </summary>
        /// <param name="rowIdx">The zero-based index of the row to delete.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// grid.AddRow(new List&lt;dynamic&gt; { "Benny is great" });
        /// 
        /// // Delete the first row
        /// grid.DeleteRow(0);
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output:
        /// // Benny is great
        /// </code>
        /// </example>
        public IAllowGridActions<T> DeleteRow(int rowIdx)
        {
            if (rowIdx >= 0 && rowIdx < data.Count)
            {
                data.RemoveAt(rowIdx);
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid row index [{rowIdx}] in DeleteRow(int). Row count is [{data.Count}]. Skipping deletion.", this, GPALObjectType.Other);
            }
            return this;
        }

        /// <summary>
        /// Deletes a row at the specified position (first or last).
        /// If the grid is empty, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and skips the operation.
        /// </summary>
        /// <param name="rowPosition">The position of the row to delete. Defaults to <see cref="RowPosition.Last"/>.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// grid.AddRow(new List&lt;dynamic&gt; { "Benny is great" });
        /// 
        /// // Delete the last row
        /// grid.DeleteRow(RowPosition.Last);
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output:
        /// // Mike me
        /// </code>
        /// </example>
        public IAllowGridActions<T> DeleteRow(RowPosition rowPosition = RowPosition.Last)
        {
            int rowIdx;

            if (data.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Grid is empty in DeleteRow(RowPosition). Skipping deletion.", this, GPALObjectType.Other);
                return this;
            }

            if (rowPosition == RowPosition.First)
            {
                rowIdx = 0;
            }
            else // position == RowPosition.Last
            {
                rowIdx = data.Count - 1;
            }

            data.RemoveAt(rowIdx);
            return this;
        }

        /// <summary>
        /// Adds a new column to the grid using a list of values.
        /// Each value in the list is added to the corresponding row. If there are fewer rows than values,
        /// new rows are created as needed.
        /// If the column is null, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and skips the operation.
        /// </summary>
        /// <param name="column">The list of values to add as a column.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike" });
        /// grid.AddRow(new List&lt;dynamic&gt; { "Benny" });
        /// 
        /// // Add a new column
        /// grid.AddColumn(new List&lt;dynamic&gt; { "25", "30" });
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output:
        /// // Mike, 25
        /// // Benny, 30
        /// </code>
        /// </example>
        public IAllowGridActions<T> AddColumn(List<T> column)
        {
            if (column == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Column is null in AddColumn. Skipping addition.", this, GPALObjectType.Other);
                return this;
            }

            for (int i = 0; i < column.Count; i++)
            {
                if (data.Count <= i)
                {
                    data.Add(new List<T>());
                }
                data[i].Add(column[i]);
            }
            return this;
        }

        /// <summary>
        /// Gets the number of rows in the grid.
        /// </summary>
        public int Rows => data.Count;

        /// <summary>
        /// Gets the number of columns in the grid.
        /// Returns 0 if the grid is empty; otherwise, returns the number of columns in the first row.
        /// </summary>
        public int Columns 
        {
            get
            {
                int count = 0;

                foreach (var row in data)
                    count = Math.Max(count, row.Count);

                return count;
            }
        }

        /// <summary>
        /// Sets the value of a specific cell in the grid.
        /// If the cell coordinates are invalid, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and skips the operation.
        /// </summary>
        /// <param name="value">The value to set in the cell.</param>
        /// <param name="row">The zero-based row index of the cell.</param>
        /// <param name="column">The zero-based column index of the cell.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike", 25 });
        /// 
        /// // Update a cell value
        /// grid.AddCell("Michael", 0, 0);
        /// 
        /// foreach (var row in grid)
        /// {
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]");
        /// }
        /// // Output:
        /// // Michael, 25
        /// </code>
        /// </example>
        public IAllowGridActions<T> AddCell(T value, int row, int column)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                data[row][column] = value;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid cell coordinates ([{row}], [{column}]) in AddCell. Grid size is [{Rows}]x[{Columns}]. Skipping operation.", this, GPALObjectType.Other);
            }
            return this;
        }

        /// <summary>
        /// Gets the value of a specific cell in the grid.
        /// If the cell coordinates are invalid, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and returns the default value for <typeparamref name="T"/>.
        /// </summary>
        /// <param name="row">The zero-based row index of the cell.</param>
        /// <param name="column">The zero-based column index of the cell.</param>
        /// <returns>The value of the cell, or the default value for <typeparamref name="T"/> if the coordinates are invalid.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike", 25 });
        /// 
        /// // Get a cell value
        /// var value = grid.GetCell(0, 0);
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{value}]"); // Output: Mike
        /// </code>
        /// </example>
        public T GetCell(int row, int column)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                return data[row][column];
            }

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid cell coordinates ([{row}], [{column}]) in GetCell. Grid size is [{Rows}]x[{Columns}]. Returning default value.", this, GPALObjectType.Other);
            return default(T);
        }

        /// <summary>
        /// Clears all rows from the grid, resetting it to an empty state.
        /// </summary>
        /// <returns>Fluent interface to continue grid operations.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// 
        /// // Clear the grid
        /// grid.Clear();
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{grid.Rows}]"); // Output: 0
        /// </code>
        /// </example>
        public IAllowGridActions<T> Clear()
        {
            data = new List<List<T>>();
            return this;
        }

        /// <summary>
        /// Gets or sets a row in the grid by its index.
        /// If the index is greater than the current number of rows, empty rows are added to accommodate the index.
        /// When setting, if the value is null, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and sets an empty list.
        /// </summary>
        /// <param name="index">The zero-based index of the row.</param>
        /// <returns>The row at the specified index.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// 
        /// // Get a row
        /// var row = grid[0];
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", row)}]"); // Output: Mike me
        /// 
        /// // Set a row
        /// grid[0] = new List&lt;dynamic&gt; { "Michael updated" };
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{string.Join(", ", grid[0])}]"); // Output: Michael updated
        /// </code>
        /// </example>
        public List<T> this[int index]
        {
            get
            {
                List<T> retVal = index < data.Count && 0 <= index ? data[index] : new List<T>();

                // A row index outside the grid returns an empty row and leaves the grid as it is. Published at DEBUG.
                if (index >= data.Count || 0 > index)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                        $"Row [{index}] was asked for and the grid holds [{data.Count}], so an empty row was answered with",
                        this, GPALObjectType.Other);

                return retVal;
            }
            set
            {
                if (index >= data.Count)
                {
                    for (int idx = index; idx >= data.Count; idx--)
                        data.Add(new List<T>());
                }

                if (value == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value is null in indexer set at index [{index}]. Setting to empty list.", this, GPALObjectType.Other);
                    data[index] = new List<T>();
                    return;
                }

                data[index] = value;
            }
        }

        /// <summary>
        /// Gets or sets the value of a specific cell in the grid using row and column indices.
        /// Internally calls <see cref="GetCell(int, int)"/> for getting and <see cref="AddCell(T, int, int)"/> for setting.
        /// </summary>
        /// <param name="row">The zero-based row index of the cell.</param>
        /// <param name="column">The zero-based column index of the cell.</param>
        /// <returns>The value of the cell when getting, or sets the value when setting.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike", 25 });
        /// 
        /// // Get a cell value
        /// var value = grid[0, 0];
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{value}]"); // Output: Mike
        /// 
        /// // Set a cell value
        /// grid[0, 0] = "Michael";
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{grid[0, 0]}]"); // Output: Michael
        /// </code>
        /// </example>
        public T this[int row, int column]
        {
            get { return GetCell(row, column); }
            set { AddCell(value, row, column); }
        }

        /// <summary>
        /// Returns the grid as a dynamic object for use within the GPAL framework.
        /// </summary>
        /// <returns>The grid instance as a dynamic object.</returns>
        public IGPALGrid<T> ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Determines whether the grid contains a specified value by comparing string representations.
        /// If the value is null, logs a warning using <see cref="GPAL.PublishSimpleEvent"/> and returns false.
        /// </summary>
        /// <param name="value">The value to search for.</param>
        /// <returns>True if the value is found in the grid; otherwise, false.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// 
        /// // Check if a value exists
        /// bool contains = grid.Contains("Mike");
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{contains}]"); // Output: True
        /// </code>
        /// </example>
        public bool Contains(T value)
        {
            if (value == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Value is null in Contains. Returning false.", this, GPALObjectType.Other);
                return false;
            }

            string valueString = value.ToString();
            foreach (List<T> list in data)
                foreach (T item in list)
                    if (item != null && item.ToString().Contains(valueString))
                        return true;
            return false;
        }

        /// <summary>
        /// Returns the number of rows in the grid.
        /// This method is an alias for the <see cref="Rows"/> property.
        /// </summary>
        /// <returns>The number of rows in the grid.</returns>
        public int Count() => data.Count();

        /// <summary>
        /// Returns an enumerator that iterates through the grid's rows.
        /// </summary>
        /// <returns>An enumerator for the grid's rows.</returns>
        public IEnumerator<List<T>> GetEnumerator()
        {
            return data.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the grid's rows (non-generic implementation).
        /// </summary>
        /// <returns>An enumerator for the grid's rows.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Creates a deep copy of the grid.
        /// If elements in the grid implement <see cref="ICloneable"/>, their <see cref="ICloneable.Clone"/> method is called.
        /// Otherwise, the elements are copied by reference.
        /// </summary>
        /// <returns>A new <see cref="GPALGrid{T}"/> instance containing a deep copy of the grid's data.</returns>
        /// <example>
        /// <code>
        /// var grid = new GPALGrid&lt;dynamic&gt;();
        /// grid.AddRow(new List&lt;dynamic&gt; { "Mike me" });
        /// 
        /// // Clone the grid
        /// var clonedGrid = (GPALGrid&lt;dynamic&gt;)grid.Clone();
        /// clonedGrid.AddRow(new List&lt;dynamic&gt; { "Cloned row" });
        /// 
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{grid.Rows}]"); // Output: 1
        /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{clonedGrid.Rows}]"); // Output: 2
        /// </code>
        /// </example>
        public object Clone()
        {
            GPALGrid<T> clone = new GPALGrid<T>();

            foreach (List<T> row in data)
            {
                List<T> clonedRow = new List<T>();
                foreach (T item in row)
                {
                    if (item is ICloneable cloneable)
                    {
                        clonedRow.Add((T)cloneable.Clone());
                    }
                    else
                    {
                        clonedRow.Add(item);
                    }
                }
                clone.AddRow(clonedRow);
            }

            return clone;
        }

        /// <summary>
        /// Loads grid data from the specified file by tokenizing its contents and replacing the grid's current data
        /// with the first tokenized list found in the file.
        /// </summary>
        /// <param name="input">The <see cref="GPALFile"/> to read and tokenize.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        public IAllowGridActions<T> InputFrom(GPALFile input)
        {
            FileHelper.TokenizeFile(input); // CAVEAT: NOTE: could tokenize multiple files? if wildcard? but we only grab top list

            // Copy cell by cell; Clone() returns a GPALGrid, which cannot be cast to List<List<T>>.
            IGPALGrid<string> tokens = ((IGPALFileInternal)input).TokenList[0];

            data = new List<List<T>>();

            foreach (List<string> row in tokens)
            {
                List<T> newRow = new List<T>();

                foreach (string cell in row)
                    newRow.Add(null == cell ? default(T) : (T)Convert.ChangeType(cell, typeof(T)));

                data.Add(newRow);
            }

            return this;
        }

        /// <summary>
        /// Saves the grid's data to the specified file using <see cref="GPAL.Converter"/>.
        /// </summary>
        /// <param name="output">The <see cref="GPALFile"/> to save the grid's data to.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        public IAllowGridActions<T> SaveTo(GPALFile output)
        {
            GPAL.Converter.WithInput(this).SaveTo(output);
            return this;
        }

        /// <summary>
        /// Loads grid data from the specified database, replacing whatever the grid held.<br/>
        /// The database runs the read it was configured with, the same one .SaveTo(out IGPALGrid&lt;string&gt;)
        /// would run, and each cell is converted to the grid's type on the way in.
        /// </summary>
        /// <param name="input">The <see cref="IGPALDatabase"/> to read data from.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        public IAllowGridActions<T> InputFrom(IGPALDatabase input)
        {
            input.SaveTo(out IGPALGrid<string> readGrid);

            data = new List<List<T>>();

            if (null != readGrid)
                foreach (List<string> row in readGrid)
                {
                    List<T> newRow = new List<T>();

                    foreach (string cell in row)
                        newRow.Add(null == cell ? default(T) : (T)Convert.ChangeType(cell, typeof(T)));

                    data.Add(newRow);
                }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Read [{data.Count}] rows from the database into the grid.", input, GPALObjectType.Database);

            return this;
        }

        /// <summary>
        /// Saves the grid's data to the specified database.<br/>
        /// The rows go in as the parameter grid, the same way .Using(grid) takes one, and the database runs the
        /// statement its verb names. Cells are written as text, so the column types on the database side decide
        /// what they end up as.
        /// </summary>
        /// <param name="output">The <see cref="IGPALDatabase"/> to write data to.</param>
        /// <returns>Fluent interface to continue grid operations.</returns>
        public IAllowGridActions<T> SaveTo(IGPALDatabase output)
        {
            IGPALGrid<string> writeGrid = GPAL.GridForType<string>();

            foreach (List<T> row in data)
            {
                List<string> newRow = new List<string>();

                foreach (T cell in row)
                    newRow.Add(null == (object)cell ? null : cell.ToString());

                writeGrid.AddRow(newRow);
            }

            output.Using(writeGrid).Execute(out int rowsWritten);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Wrote [{rowsWritten}] rows from the grid to the database.", output, GPALObjectType.Database);

            return this;
        }

        /// <summary>
        /// Flattens the grid into a single list by concatenating all cell values from every row, in row-major order.
        /// </summary>
        /// <returns>A flat <see cref="List{T}"/> containing every cell value in the grid.</returns>
        public List<T> ToList()
        {
            List<T> retVal = new List<T>();

            foreach (List<T> row in data)
                foreach (T col in row)
                    retVal.Add(col);

            return retVal;
        }
    }
}
