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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using static GenerallyPositive.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectGraphTraversalStrategies;
using YamlDotNet.Serialization.ObjectFactories;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Collections;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace GenerallyPositive
{
    public partial class GPALConverter
    {
        /// <summary>
        /// Converts the configured input to database format and writes the resulting rows into the given database.
        /// </summary>
        /// <param name="outputDatabase">The database to write the converted rows into</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions SaveTo(IGPALDatabase outputDatabase)
        {
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClass = null;
            ConverterSettings.OutputClassType = null;
            ConverterSettings.OutputClassElementType = null;
            ConverterSettings.OutputGrid = null;

            ConverterSettings.OutputDatabase = outputDatabase;
            ConverterSettings.OutDataFormat = DataFormat.DATABASE;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data to database.", this, GPALObjectType.Converter);
            return Convert(ref outputDatabase);
        }
        /// <summary>
        /// Converts the configured input to GRID format and writes the resulting rows/columns into the given grid.
        /// </summary>
        /// <param name="outputGrid">The grid to receive the converted rows; also used to determine the output element type</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions SaveTo(ref IGPALGrid<string> outputGrid)
        {
            ConverterSettings.OutputDatabase = null;
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClassType = outputGrid.GetType();
            ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetElementType();
            if (null == ConverterSettings.OutputClassElementType && 0 < ConverterSettings.OutputClassType.GetGenericArguments().Length)
                if (true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType))
                    ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetGenericArguments()[1];
                else
                    ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetGenericArguments()[0];
            ConverterSettings.OutputGrid = outputGrid;
            ConverterSettings.OutDataFormat = DataFormat.GRID;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data to grid.", this, GPALObjectType.Converter);

            Convert(ref outputGrid);

            return this;
        }
        /// <summary>
        /// Converts the configured input and appends the resulting data to the given output file (instead of overwriting it).
        /// </summary>
        /// <param name="outputFile">The file to append the converted data to; its data format is determined from its file extension</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions AppendTo(GPALFile outputFile)
        {
            ConverterSettings.OutputFile = outputFile;
            ConverterSettings.OutDataFormat = ConverterHelper.GetDataFormatFromExtension(outputFile.Filename);    // will iterate files in convert and determine each dataformat
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{ConverterSettings.InDataFormat.FirstOrDefault()}] data as [{ConverterSettings.OutDataFormat}] to [{outputFile.Filename}].", this, GPALObjectType.Converter);
            return Convert(ref outputFile, true);
        }
        /// <summary>
        /// Converts the configured input and writes (overwrites) the resulting data to the given output file.
        /// </summary>
        /// <param name="outputFile">The file to write the converted data to; its data format is determined from its file extension</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        /// <example>
        /// <code>
        /// GPAL.Converter
        ///     .WithInput((GPALFile)"input.csv")
        ///     .SaveTo((GPALFile)"output.json");
        /// </code>
        /// </example>
        public IAllowConverterInputAndActions SaveTo(GPALFile outputFile)
        {
            ConverterSettings.OutputFile = outputFile;
            ConverterSettings.OutDataFormat = ConverterHelper.GetDataFormatFromExtension(outputFile.Filename);    // will iterate files in convert and determine each dataformat
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data as [{ConverterSettings.OutDataFormat}] to [{outputFile.Filename}].", this, GPALObjectType.Converter);
            Convert(ref outputFile);
            outputFile.ReturnFilenames.Add(outputFile.Filename);    // return the filename we actually save to as a filename
            return this;
        }
        // Each collection saved to, with the input it was filled from, known by the list of formats each WithInput makes
        // new, the rows the last save from that input added and those rows as json text. A collection given as the input
        // gets nothing added. A save from the same input whose rows are, by value, the rows the last one added, while the
        // collection still holds those rows, adds nothing, and the collection is put back as it was; rows that differ,
        // as a file or database read again after it changed gives, are added. A save that adds no rows leaves the last
        // rows known. The table holds no collection alive.
        private readonly ConditionalWeakTable<object, Tuple<object, object[], string>> filledFromInput = new ConditionalWeakTable<object, Tuple<object, object[], string>>();
        public IAllowConverterInputAndActions SaveTo<T>(ref T outputClass)
        {
            // a nullable value that holds nothing yet, such as an int? left null, converts to its declared type
            Type outputType = null == outputClass ? typeof(T) : outputClass.GetType();
            Type collectionBase = ConverterHelper.GenericCollectionBase(outputType);
            Type readOnlyItemType = null == collectionBase && null != outputClass ? ConverterHelper.ReadOnlyItemType(outputClass) : null;
            // the AddRow of its own a class derived from a generic collection has, taking a List of its item
            MethodInfo rowAdder = null == collectionBase ? null
                : outputType.GetMethod("AddRow", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(List<>).MakeGenericType(ConverterHelper.CollectionItemType(collectionBase)) }, null);
            bool isCollection = outputClass is IEnumerable && false == outputClass is string && false == outputClass is Array;
            bool holdsInputRows = true == isCollection && true == ReferenceEquals(outputClass, (object)ConverterSettings.InputClass);
            // the collection and its items before this save, to know the rows it adds and to put it back
            object collectionBefore = outputClass;
            object[] itemsBefore = true == isCollection ? ((IEnumerable)outputClass).Cast<object>().ToArray() : new object[0];
            // the last save from this input, when the collection still holds each row it added
            Tuple<object, object[], string> lastSave = null;
            List<object> unmatchedBefore = itemsBefore.ToList();
            bool holdsLastRows = true == isCollection && false == holdsInputRows
                && true == filledFromInput.TryGetValue(outputClass, out lastSave) && true == ReferenceEquals(lastSave.Item1, ConverterSettings.InDataFormat)
                && true == lastSave.Item2.All(lastRow => true == unmatchedBefore.Remove(lastRow));

            ConverterSettings.OutputFile = null;
            SetOutputClassType(outputType);

            ConverterSettings.OutDataFormat = DataFormat.CLASS;

            if (true == holdsInputRows)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{outputType.Name}] holds the rows of this input already, so they are not added again", this, GPALObjectType.Converter);
            // A class derived from a generic collection with an AddRow of its own, such as Bag : List<string> with
            // AddRow(List<string>), is given each row of the input through that AddRow, after the items it holds
            else if (null != rowAdder)
            {
                object rows = Activator.CreateInstance(typeof(List<>).MakeGenericType(rowAdder.GetParameters()[0].ParameterType));

                SaveTo(ref rows);

                if (null != rows)
                    foreach (object row in (IEnumerable)rows)
                        rowAdder.Invoke(outputClass, new[] { row });
            }
            // A class derived from a generic collection, such as Items : List<Item>, is filled as that collection is: the
            // collection takes the class's items, is converted into, and gives its items back to the class through its
            // own Add, Enqueue, Push or AddLast; nothing, as the collection gets, when the conversion gives nothing
            else if (null != collectionBase)
            {
                System.Reflection.MethodInfo addItem = ConverterHelper.CollectionAddMethod(collectionBase);
                object filled = Activator.CreateInstance(collectionBase);

                // a stack lists its top first, so its items are pushed from the bottom up
                List<object> InAddOrder(object collection)
                {
                    List<object> items = ((System.Collections.IEnumerable)collection).Cast<object>().ToList();
                    if ("Push" == addItem.Name)
                        items.Reverse();
                    return items;
                }

                foreach (object item in InAddOrder(outputClass))
                    addItem.Invoke(filled, new[] { item });

                SaveTo(ref filled);

                if (null == filled)
                    outputClass = default(T);
                else
                {
                    collectionBase.GetMethod("Clear", Type.EmptyTypes).Invoke(outputClass, null);
                    foreach (object item in InAddOrder(filled))
                        addItem.Invoke(outputClass, new[] { item });
                }
            }
            // A read-only collection, such as ReadOnlyCollection<Item>, cannot be added to, so it is made new from the
            // converted items through its constructor that takes them; one with no such constructor is left as it was
            else if (null != readOnlyItemType)
            {
                System.Reflection.ConstructorInfo makeWith = ConverterHelper.ItemsConstructor(outputClass.GetType(), readOnlyItemType);

                if (null == makeWith)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{outputClass.GetType().Name}] is read-only and has no constructor that takes its items, so it is left as it was", this, GPALObjectType.Converter);
                else
                {
                    object items = Activator.CreateInstance(typeof(List<>).MakeGenericType(readOnlyItemType));

                    SaveTo(ref items);

                    outputClass = null == items ? default(T) : (T)ConverterHelper.MakeFromItems(makeWith, items);
                }
            }
            // Several files go one at a time through the one-file conversion. A dictionary whose key can be a string,
            // keyed by string or object or a Hashtable, takes each file under its file name, and one keyed by a whole
            // number under its position, 0 for the first file; another dictionary converts them all at once.
            else if (null != ConverterSettings.InputFile && 1 < ConverterSettings.InputFile.Filenames.Count
                && true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType)
                && (true == ConverterHelper.DictionaryTypeArguments(ConverterSettings.OutputClassType)[0].IsAssignableFrom(typeof(string))
                    || true == WholeNumberKeyTypes.Contains(ConverterHelper.DictionaryTypeArguments(ConverterSettings.OutputClassType)[0])))
                ConvertFilesToDictionary(ref outputClass);
            else if (null != ConverterSettings.InputFile && 1 < ConverterSettings.InputFile.Filenames.Count
                && false == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType))
                ConvertFileByFile(ref outputClass);
            else
            {
                // A copy from one object to another holds the links back to an object holding them, as the object does.
                // The object's records hold each such link as a record of its placeholder, {"Parent": "Recursion to
                // [Root]"}, which the copy would make an empty object of or leave out with its list. For the copy each
                // is null, which its member, list or dictionary keeps, and each null is then set to the copy of the
                // object it links to. The records are put back as they were for the conversions after this one
                bool fromObject = 1 == ConverterSettings.InDataFormat.Count && DataFormat.CLASS == ConverterSettings.InDataFormat[0]
                    && null != (object)ConverterSettings.InputClass && null != ConverterSettings.InputDictionary;
                List<Action> putBack = new List<Action>();
                HashSet<object> recordsWalked = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);

                // a record that holds only the placeholder of a link
                bool IsLink(object value) => value is IDictionary record && 1 == record.Count
                    && record.Values.Cast<object>().First() is string linkText
                    && true == linkText.StartsWith("Recursion to [", StringComparison.Ordinal) && true == linkText.EndsWith("]", StringComparison.Ordinal);

                // each link in the records made null, and how to put it back
                void NullLinks(object records)
                {
                    if (records is IDictionary entries && true == recordsWalked.Add(records))
                        foreach (object key in entries.Keys.Cast<object>().ToList())
                        {
                            object entry = entries[key];
                            if (true == IsLink(entry))
                            {
                                entries[key] = null;
                                putBack.Add(() => entries[key] = entry);
                            }
                            else
                                NullLinks(entry);
                        }
                    else if (records is IList items && true == recordsWalked.Add(records))
                        for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                        {
                            object item = items[itemIdx];
                            int at = itemIdx;
                            if (true == IsLink(item))
                            {
                                items[at] = null;
                                putBack.Add(() => items[at] = item);
                            }
                            else
                                NullLinks(item);
                        }
                }

                // an object a place typed object holds is copied too, the source on the path of that copy from the start
                try
                {
                    if (true == fromObject)
                    {
                        foreach (Dictionary<object, dynamic> records in ConverterSettings.InputDictionary)
                            NullLinks(records);
                        ConverterHelper.objectsBeingCopied = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance) { ConverterSettings.InputClass };
                    }
                    Convert(ref outputClass);
                }
                finally
                {
                    putBack.ForEach(put => put());
                    ConverterHelper.objectsBeingCopied = null;
                }

                // Each object of the source on the path walked, and its copy: a link points back to one of them. Each
                // object of the copy is walked once
                Dictionary<object, object> onPath = new Dictionary<object, object>(ConverterHelper.ReferenceEqualityComparer.Instance);
                HashSet<object> copiesWalked = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);

                // fills the links within an object of the source and its copy, and says whether it filled any
                bool Fill(object from, object into)
                {
                    bool filled = false;
                    Type fromType = from?.GetType();
                    // an object the copy holds as the source's own, as a place typed object holds it, is the source's and
                    // is not changed
                    bool walk = null != from && null != into && false == ReferenceEquals(from, into) && true == ConverterHelper.CanTakePartInACycle(fromType)
                        && (true == fromType.IsValueType || (false == onPath.ContainsKey(from) && (true == into.GetType().IsValueType || true == copiesWalked.Add(into))));
                    bool tracked = true == walk && false == fromType.IsValueType;
                    if (true == tracked)
                        onPath.Add(from, into);

                    // One place of the copy where the source links back to an object on the path takes that object's
                    // copy: null, or the source's object itself, as a place typed object holds it. A value has its own
                    // links filled. A struct's value is a boxed copy of the one held there, so it is put back when
                    // filled. Says whether the place or the value in it changed
                    bool Place(object fromValue, object intoValue, Type slotType, Func<object, bool> put)
                    {
                        bool placed = false;
                        if (null != fromValue && (null == intoValue || true == ReferenceEquals(intoValue, fromValue))
                            && true == onPath.TryGetValue(fromValue, out object linkedCopy)
                            && true == slotType.IsAssignableFrom(linkedCopy.GetType()))
                            placed = put(linkedCopy);
                        else if (true == Fill(fromValue, intoValue))
                        {
                            placed = true;
                            if (true == intoValue.GetType().IsValueType)
                                put(intoValue);
                        }
                        return placed;
                    }

                    // dictionary entries are paired by key
                    if (true == walk && from is IDictionary fromEntries && into is IDictionary intoEntries)
                    {
                        Type valueType = ConverterHelper.DictionaryTypeArguments(into.GetType())[1];
                        foreach (DictionaryEntry entry in fromEntries)
                            if (null != entry.Key && true == intoEntries.Contains(entry.Key))
                                filled = true == Place(entry.Value, intoEntries[entry.Key], valueType, value =>
                                {
                                    bool put = false == intoEntries.IsReadOnly;
                                    if (true == put)
                                        intoEntries[entry.Key] = value;
                                    return put;
                                }) || filled;
                    }
                    // a list's items are paired by place when the copy holds as many; an array of more than one
                    // dimension has no place of one index
                    else if (true == walk && from is IList fromItems && into is IList intoItems && fromItems.Count == intoItems.Count
                        && false == (from is Array fromArray && 1 != fromArray.Rank) && false == (into is Array intoArray && 1 != intoArray.Rank))
                    {
                        Type itemType = ConverterHelper.CollectionItemType(into.GetType());
                        for (int itemIdx = 0; itemIdx < fromItems.Count; itemIdx++)
                        {
                            int at = itemIdx;
                            filled = true == Place(fromItems[at], intoItems[at], itemType, value =>
                            {
                                bool put = false == intoItems.IsReadOnly;
                                if (true == put)
                                    intoItems[at] = value;
                                return put;
                            }) || filled;
                        }
                    }
                    // members are paired by name, the same name first, else the same name in another case, as the copy
                    // maps them; a set, queue or stack, whose items have no place to pair them by, is not walked
                    else if (true == walk && false == from is IEnumerable)
                    {
                        MemberInfo[] intoMembers = ConverterHelper.CyclableMembers(into.GetType());
                        foreach (MemberInfo fromMember in ConverterHelper.CyclableMembers(fromType))
                        {
                            MemberInfo intoMember = intoMembers.FirstOrDefault(member => member.Name == fromMember.Name)
                                ?? intoMembers.FirstOrDefault(member => true == string.Equals(member.Name, fromMember.Name, StringComparison.OrdinalIgnoreCase));
                            PropertyInfo intoProperty = intoMember as PropertyInfo;
                            if (null != intoMember)
                                filled = true == Place(ConverterHelper.MemberValue(fromMember, from), ConverterHelper.MemberValue(intoMember, into),
                                    null != intoProperty ? intoProperty.PropertyType : ((FieldInfo)intoMember).FieldType, value =>
                                    {
                                        bool put = null != intoProperty ? null != intoProperty.GetSetMethod() : false == ((FieldInfo)intoMember).IsInitOnly;
                                        try
                                        {
                                            if (true == put && null != intoProperty)
                                                intoProperty.SetValue(into, value);
                                            else if (true == put)
                                                ((FieldInfo)intoMember).SetValue(into, value);
                                        }
                                        catch (Exception)
                                        {
                                            // a setter that throws is the type's business, not the walk's
                                            put = false;
                                        }
                                        return put;
                                    }) || filled;
                        }
                    }

                    if (true == tracked)
                        onPath.Remove(from);
                    return filled;
                }

                // a struct's copy is filled boxed and put back
                object copy = outputClass;
                if (true == fromObject && true == Fill((object)ConverterSettings.InputClass, copy) && true == copy.GetType().IsValueType)
                    outputClass = (T)copy;
            }

            // The rows this save added are the items the collection holds now that it did not hold before, compared as
            // json text with the rows the last save from this input added. A member that cannot be written to json is
            // passed over in that text, so it does not stop the save
            if (false == holdsInputRows && outputClass is IEnumerable filledItems && false == outputClass is string && false == outputClass is Array)
            {
                List<object> notAdded = itemsBefore.ToList();
                object[] addedRows = filledItems.Cast<object>().Where(item => false == notAdded.Remove(item)).ToArray();
                string addedText = JsonConvert.SerializeObject(addedRows, new JsonSerializerSettings
                {
                    Converters = { new IPAddressJsonConverter(), new ComplexJsonConverter(), new UriJsonConverter(), new VersionJsonConverter() },
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Error = (sender, failure) => failure.ErrorContext.Handled = true
                });
                MethodInfo clearItems = outputType.GetMethod("Clear", Type.EmptyTypes);
                MethodInfo addItemBack = ConverterHelper.CollectionAddMethod(outputType);
                bool canPutBack = false == ReferenceEquals(outputClass, collectionBefore) || outputClass is IDictionary || (null != clearItems && null != addItemBack);

                if (true == holdsLastRows && 0 < addedRows.Length && addedText == lastSave.Item3 && true == canPutBack)
                {
                    // a collection made new by the save is the one it was; one added to is cleared and given its items back
                    if (false == ReferenceEquals(outputClass, collectionBefore))
                        outputClass = (T)collectionBefore;
                    else if (outputClass is IDictionary entries)
                    {
                        entries.Clear();
                        foreach (object entry in itemsBefore)
                            entries.Add(entry.GetType().GetProperty("Key").GetValue(entry), entry.GetType().GetProperty("Value").GetValue(entry));
                    }
                    else
                    {
                        clearItems.Invoke(outputClass, null);
                        // a stack lists its top first, so its items are pushed from the bottom up
                        foreach (object item in "Push" == addItemBack.Name ? Enumerable.Reverse(itemsBefore) : itemsBefore)
                            addItemBack.Invoke(outputClass, new[] { item });
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{outputType.Name}] holds the rows of this input already, so they are not added again", this, GPALObjectType.Converter);
                }
                else
                {
                    // A dictionary the save made new is the converted entries: the dictionary given keeps its entries and
                    // takes them, as a list keeps its items, and a key it holds already takes the new value, with a warning
                    // naming the key
                    if (false == ReferenceEquals(outputClass, collectionBefore) && collectionBefore is IDictionary heldEntries
                        && (object)outputClass is IDictionary convertedEntries && false == heldEntries.IsReadOnly)
                    {
                        // a key held already with an equal value is left as it is
                        foreach (DictionaryEntry convertedEntry in convertedEntries)
                        {
                            bool keyHeld = true == heldEntries.Contains(convertedEntry.Key);
                            if (true == keyHeld && false == Equals(heldEntries[convertedEntry.Key], convertedEntry.Value))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{outputType.Name}] holds the key [{convertedEntry.Key}] already, so its value is replaced", this, GPALObjectType.Converter);
                            if (false == keyHeld || false == Equals(heldEntries[convertedEntry.Key], convertedEntry.Value))
                                heldEntries[convertedEntry.Key] = convertedEntry.Value;
                        }
                        outputClass = (T)collectionBefore;
                    }
                    if (0 < addedRows.Length)
                    {
                        filledFromInput.Remove(outputClass);
                        filledFromInput.Add(outputClass, Tuple.Create((object)ConverterSettings.InDataFormat, addedRows, addedText));
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Sets the type converted to and the type of its elements: an array's element, a dictionary's value, a grid's
        /// row, or a collection's item.
        /// </summary>
        /// <param name="outputClassType">The type converted to</param>
        private void SetOutputClassType(Type outputClassType)
        {
            ConverterSettings.OutputClassType = outputClassType;
            ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetElementType();

            if (ConverterSettings.OutputClassElementType == null)
            {
                var genericArgs = ConverterSettings.OutputClassType.GetGenericArguments();

                if (genericArgs.Length > 0)
                {
                    // Check if it's a Dictionary<TKey, TValue>
                    if (ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType))
                    {
                        ConverterSettings.OutputClassElementType = ConverterHelper.DictionaryTypeArguments(ConverterSettings.OutputClassType)[1]; // TValue, from the IDictionary it is, so MyDict<T> : Dictionary<string, T> gives T
                    }
                    // Check if it's any IGPALGrid<T>
                    else if (ConverterSettings.OutputClassType.IsGenericType &&
                             ConverterSettings.OutputClassType.GetGenericTypeDefinition() == typeof(IGPALGrid<>))
                    {
                        // For IGPALGrid<T>, the "element" type is List<T>
                        var elementT = genericArgs[0]; // T
                        ConverterSettings.OutputClassElementType = typeof(List<>).MakeGenericType(elementT);
                    }
                    // Fallback: assume it's IEnumerable<T> or similar; Foo<A, B> : List<B> holds B
                    else
                    {
                        ConverterSettings.OutputClassElementType = ConverterHelper.CollectionItemType(ConverterSettings.OutputClassType);
                    }
                }
                // a subclass of a generic dictionary has no type arguments of its own; its element is the value type it
                // takes
                else if (true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType)
                    && true == ConverterSettings.OutputClassType.GetInterfaces().Any(type => true == type.IsGenericType && typeof(IDictionary<,>) == type.GetGenericTypeDefinition()))
                {
                    ConverterSettings.OutputClassElementType = ConverterHelper.DictionaryTypeArguments(ConverterSettings.OutputClassType)[1];
                }
            }
        }
        // the dictionary key types that take several files by position
        private static readonly Type[] WholeNumberKeyTypes = { typeof(int), typeof(long), typeof(short), typeof(byte), typeof(uint), typeof(ulong), typeof(ushort), typeof(sbyte) };
        // the input a conversion to one object filled it from, the records as read, set by Convert and read by
        // ConvertFilesToDictionary for each file; null when the object was not filled from records
        private object singleObjectInput;
        /// <summary>
        /// Converts a GPALFile naming several files into a dictionary keyed by file name, or for a whole number key by
        /// position, 0 for the first file, each file converted on its own, with its own settings, into one value of the
        /// dictionary's value type: one object for a json, yaml or xml file holding one, or a list for a file of rows. A
        /// file that is not there, or one that fills no member of a class value, adds nothing, with a warning. A file
        /// whose name is already a key is keyed by its path from the folder of the first file, with a warning.
        /// </summary>
        /// <typeparam name="T">The dictionary type, keyed by string, object or a whole number, or a Hashtable</typeparam>
        /// <param name="outputClass">The dictionary to fill</param>
        private void ConvertFilesToDictionary<T>(ref T outputClass)
        {
            GPALFile allFiles = ConverterSettings.InputFile;
            List<DataFormat> allFormats = ConverterSettings.InDataFormat;
            Type dictionaryType = ConverterSettings.OutputClassType;
            Type[] dictionaryArgs = ConverterHelper.DictionaryTypeArguments(dictionaryType);
            Type keyType = dictionaryArgs[0];
            bool keyedByName = keyType.IsAssignableFrom(typeof(string));
            Type valueType = dictionaryArgs[1];
            System.Collections.IDictionary dictionary = (System.Collections.IDictionary)outputClass;

            // a list interface, such as IList<Person>, is filled as a List of its item type
            Type madeValueType = true == valueType.IsInterface && true == valueType.IsGenericType && 1 == valueType.GetGenericArguments().Length
                && true == valueType.IsAssignableFrom(typeof(List<>).MakeGenericType(valueType.GetGenericArguments()[0]))
                ? typeof(List<>).MakeGenericType(valueType.GetGenericArguments()[0])
                : valueType;

            // each file is converted into a new value, so the value type has to be one that can be made empty
            bool canMakeValue = true == madeValueType.IsArray || typeof(string) == madeValueType || true == madeValueType.IsValueType
                || null != madeValueType.GetConstructor(Type.EmptyTypes);

            FillColumnNamesFrom(allFiles);

            if (false == canMakeValue)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{valueType.Name}] has no constructor without parameters, so the files cannot each be converted into one; nothing is added", this, GPALObjectType.Converter);

            try
            {
                for (int fileIdx = 0; fileIdx < allFiles.Filenames.Count && true == canMakeValue; fileIdx++)
                {
                    // a missing file keeps its position, so the files after it keep theirs
                    object key = true == keyedByName ? (object)Path.GetFileName(allFiles.Filenames[fileIdx]) : System.Convert.ChangeType(fileIdx, keyType);

                    // A file whose name is already a key, such as sub\k.json after k.json, is keyed by its path from the
                    // folder of the first file: the folders the two full paths share are taken off, and each folder of
                    // the first file's past them is a ..\ step
                    if (true == keyedByName && true == dictionary.Contains(key) && true == File.Exists(allFiles.Filenames[fileIdx]))
                    {
                        string[] fromFolders = Path.GetDirectoryName(Path.GetFullPath(allFiles.Filenames[0])).TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
                        string[] toParts = Path.GetFullPath(allFiles.Filenames[fileIdx]).Split(Path.DirectorySeparatorChar);
                        int shared = 0;

                        while (shared < fromFolders.Length && shared < toParts.Length - 1
                            && true == string.Equals(fromFolders[shared], toParts[shared], StringComparison.OrdinalIgnoreCase))
                            shared++;

                        string pathKey = 0 == shared
                            ? Path.GetFullPath(allFiles.Filenames[fileIdx])
                            : string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("..", fromFolders.Length - shared).Concat(toParts.Skip(shared)));
                        string namedBy = allFiles.Filenames.Take(fileIdx).FirstOrDefault(earlier => true == string.Equals(Path.GetFileName(earlier), (string)key, StringComparison.OrdinalIgnoreCase));

                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{key}] is the name of [{namedBy ?? key}] and [{allFiles.Filenames[fileIdx]}], so [{allFiles.Filenames[fileIdx]}] is keyed by its path [{pathKey}]", this, GPALObjectType.Converter);
                        key = pathKey;
                    }

                    if (false == File.Exists(allFiles.Filenames[fileIdx]))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"File missing: [{allFiles.Filenames[fileIdx]}], so it adds nothing. Continuing.", this, GPALObjectType.Converter);
                    else if (true == dictionary.Contains(key))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The dictionary already holds [{key}], so [{allFiles.Filenames[fileIdx]}] adds nothing; the dictionary is keyed by file {(true == keyedByName ? "name" : "position")}", this, GPALObjectType.Converter);
                    else
                    {
                        ConverterSettings.InputFile = OneFileOf(allFiles, fileIdx);
                        ConverterSettings.InDataFormat = new List<DataFormat> { allFormats[fileIdx] };
                        SetOutputClassType(madeValueType);

                        object value = true == madeValueType.IsArray ? Array.CreateInstance(madeValueType.GetElementType(), 0)
                            : typeof(string) == madeValueType ? string.Empty
                            : Activator.CreateInstance(madeValueType);

                        // the conversion before this one leaves its result behind, and a file that gives nothing would
                        // be stored as that result
                        ConverterSettings.OutputClass = null;
                        singleObjectInput = null;
                        // a value type derived from a list, such as Items : List<Item>, goes through SaveTo, which fills
                        // it as the list it derives from
                        if (null != ConverterHelper.GenericCollectionBase(madeValueType))
                            SaveTo(ref value);
                        else
                            Convert(ref value);

                        // A file of rows converted to an object is stored as the list of its rows, a file of one row
                        // too: the rows the reader keyed GPALKEY and their place, else the converted value's rows under
                        // those keys. A file that is one object, such as a json {...}, is that object.
                        System.Collections.IDictionary fileRows = singleObjectInput is System.Collections.IList readInput && 1 == readInput.Count
                            && readInput[0] is System.Collections.IDictionary readRows && 0 < readRows.Count
                            && true == readRows.Keys.Cast<object>().All(rowKey => true == rowKey?.ToString().StartsWith("GPALKEY"))
                            ? readRows
                            : value as System.Collections.IDictionary;

                        if (typeof(object) == madeValueType && null != fileRows && 0 < fileRows.Count
                            && true == fileRows.Keys.Cast<object>().All(rowKey => true == rowKey?.ToString().StartsWith("GPALKEY")))
                            value = fileRows.Values.Cast<object>().ToList();

                        // A file that fills no member of a class value, such as an html page or an empty file given a
                        // Person, adds nothing, as a row that fills nothing is not added to a list. Its records are
                        // checked as read, leaving out the empty record an empty file is read as.
                        object fileRecords = singleObjectInput is System.Collections.IList records
                            ? records.Cast<object>().Where(record => false == (record is System.Collections.IDictionary fields && 0 == fields.Count)).ToList()
                            : singleObjectInput;
                        bool fillsNothing = null != singleObjectInput
                            && true == ConverterHelper.FillsNothing(fileRecords, madeValueType, ConverterSettings.ColumnNames?[0], null);

                        if (true == fillsNothing)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{allFiles.Filenames[fileIdx]}] fills no member of [{valueType.Name}], so it adds nothing", this, GPALObjectType.Converter);
                        else if (true == valueType.IsInstanceOfType(value))
                            dictionary[key] = value;
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{allFiles.Filenames[fileIdx]}] converted to [{value?.GetType().Name ?? "nothing"}], not [{valueType.Name}], so it adds nothing", this, GPALObjectType.Converter);
                    }
                }
            }
            finally
            {
                ConverterSettings.InputFile = allFiles;
                ConverterSettings.InDataFormat = allFormats;
                SetOutputClassType(dictionaryType);
                singleObjectInput = null;
            }
        }
        /// <summary>
        /// Fills ColumnNames from every file's column list when the caller gave none and the first file is delimited, as
        /// a conversion of all the files at once does, so it stays for a later SaveTo in the chain.
        /// </summary>
        /// <param name="allFiles">The GPALFile naming several files</param>
        private void FillColumnNamesFrom(GPALFile allFiles)
        {
            if (ConverterHelper.IsDelimitedDataFormat(ConverterHelper.GetDataFormatFromExtension(allFiles.Filename))
                && 0 == ConverterSettings.ColumnNames.Count())
            {
                // a file whose unit gave no column names has an empty row, which names nothing
                ConverterSettings.ColumnNames = new GPALGrid<string>();
                foreach (List<string> colRow in ((IGPALFileInternal)allFiles).FileSettings.ColumnList)
                    if (0 < colRow.Count)
                        ConverterSettings.ColumnNames.AddRow(new List<string>(colRow));
            }
        }
        /// <summary>
        /// Converts a GPALFile naming several files one file at a time, each with its own settings, so one file is held
        /// at a time. One object is filled from the first file, with a warning to save to a list for the rest. A list,
        /// array, queue, stack, set or grid gets each file's items added in file order.
        /// </summary>
        /// <typeparam name="T">The type converted to</typeparam>
        /// <param name="outputClass">The object or collection to fill</param>
        private void ConvertFileByFile<T>(ref T outputClass)
        {
            GPALFile allFiles = ConverterSettings.InputFile;
            List<DataFormat> allFormats = ConverterSettings.InDataFormat;
            bool oneObject = false == outputClass is Array && (false == outputClass is System.Collections.IEnumerable || outputClass is string);
            int fileCount = true == oneObject ? 1 : allFiles.Filenames.Count;

            if (true == oneObject)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ConverterSettings.OutputClassType.Name}] holds one object, so it is filled from the first of [{allFiles.Filenames.Count}] files; save to a list to take them all", this, GPALObjectType.Converter);

            FillColumnNamesFrom(allFiles);

            try
            {
                for (int fileIdx = 0; fileIdx < fileCount; fileIdx++)
                {
                    ConverterSettings.InputFile = OneFileOf(allFiles, fileIdx);
                    ConverterSettings.InDataFormat = new List<DataFormat> { allFormats[fileIdx] };

                    // the conversion before this one leaves its result behind, and a file that gives nothing would add
                    // that result again
                    ConverterSettings.OutputClass = null;

                    // a file that is not there is passed over, so the file before it is not converted twice
                    if (false == File.Exists(allFiles.Filenames[fileIdx]))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"File missing: [{allFiles.Filenames[fileIdx]}], so it adds nothing. Continuing.", this, GPALObjectType.Converter);
                    else if (true == oneObject || 0 == fileIdx)
                        // the first file converts into the caller's object, as one file does
                        Convert(ref outputClass);
                    else
                    {
                        T part = (T)(outputClass is Array
                            ? (object)Array.CreateInstance(outputClass.GetType().GetElementType(), 0)
                            : Activator.CreateInstance(outputClass.GetType()));

                        Convert(ref part);
                        outputClass = AddItems(outputClass, part);
                    }
                }
            }
            finally
            {
                ConverterSettings.InputFile = allFiles;
                ConverterSettings.InDataFormat = allFormats;
            }
        }
        /// <summary>
        /// A GPALFile naming one of the files of another, with that file's own delimiter, quote, first line and column
        /// name settings; column names are the first list given when one is used for all files.
        /// </summary>
        /// <param name="allFiles">The GPALFile naming several files</param>
        /// <param name="fileIdx">Which of its files</param>
        /// <returns>A GPALFile naming that file alone</returns>
        private static GPALFile OneFileOf(GPALFile allFiles, int fileIdx)
        {
            GPALFileSettings from = ((IGPALFileInternal)allFiles).FileSettings;
            GPALFile one = new GPALFile(from.Filenames[fileIdx]);
            GPALFileSettings to = ((IGPALFileInternal)one).FileSettings;

            if (fileIdx < from.Delimiter.Count && true == from.Delimiter[fileIdx].HasValue)
                to.Delimiter.Add(from.Delimiter[fileIdx]);

            if (fileIdx < from.FieldsEnclosedInQuotes.Count)
                to.FieldsEnclosedInQuotes.Add(from.FieldsEnclosedInQuotes[fileIdx]);

            if (fileIdx < from.FirstLineGiven.Count && true == from.FirstLineGiven[fileIdx])
            {
                to.FirstLineIsColumnNames.Add(from.FirstLineIsColumnNames[fileIdx]);
                to.FirstLineGiven.Add(true);
            }

            if (fileIdx < from.IgnoreFirstLineColumnNames.Count)
                to.IgnoreFirstLineColumnNames.Add(from.IgnoreFirstLineColumnNames[fileIdx]);

            // the file's own column names, or the first list given when one is used for all files
            List<string> columns = true == from.UseOneHeaderForAllFiles
                ? ((IEnumerable<List<string>>)from.ColumnList).FirstOrDefault(row => 0 < row.Count)
                : fileIdx < from.ColumnList.Count() ? from.ColumnList[fileIdx] : null;

            if (null != columns && 0 < columns.Count)
                to.ColumnList.AddRow(new List<string>(columns));

            to.UseOneHeaderForAllFiles = from.UseOneHeaderForAllFiles;

            return one;
        }
        /// <summary>
        /// Adds the items of one file's result to the result so far: an array is joined, a grid gets the rows, and a
        /// list, set, queue, stack or LinkedList gets each item through Add, Enqueue, Push or AddLast, a stack in the order
        /// it was read.
        /// </summary>
        /// <typeparam name="T">The collection type</typeparam>
        /// <param name="total">The result so far</param>
        /// <param name="part">One file's result</param>
        /// <returns>The result with the file's items added</returns>
        private static T AddItems<T>(T total, T part)
        {
            T retVal = total;
            Type type = total.GetType();

            if (total is Array totalArray && part is Array partArray)
            {
                Array joined = Array.CreateInstance(type.GetElementType(), totalArray.Length + partArray.Length);

                totalArray.CopyTo(joined, 0);
                partArray.CopyTo(joined, totalArray.Length);
                retVal = (T)(object)joined;
            }
            else
            {
                Type elementType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
                System.Reflection.MethodInfo addRow = type.GetMethod("AddRow", new[] { typeof(List<>).MakeGenericType(elementType) });
                System.Reflection.MethodInfo add = type.GetMethod("Add", new[] { elementType }) ?? type.GetMethod("Enqueue", new[] { elementType }) ?? type.GetMethod("AddLast", new[] { elementType });
                System.Reflection.MethodInfo push = type.GetMethod("Push", new[] { elementType });
                System.Reflection.MethodInfo addItem = addRow ?? add ?? push;
                List<object> items = ((System.Collections.IEnumerable)part).Cast<object>().ToList();

                // A stack reads from its top, and one file's stack has its first row on top. So the stack is built again
                // from both, the earlier file's items on top, pushed from the bottom up.
                if (null == addRow && null == add && null != push)
                {
                    items = ((System.Collections.IEnumerable)total).Cast<object>().Concat(items).ToList();
                    items.Reverse();
                    type.GetMethod("Clear", Type.EmptyTypes).Invoke(total, null);
                }

                if (null == addItem)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{type.Name}] has no AddRow, Add, Enqueue, Push or AddLast, so the items of the files after the first are not added", null, GPALObjectType.Other);
                else
                    foreach (object item in items)
                        addItem.Invoke(total, new[] { item });
            }

            return retVal;
        }
        /// <summary>
        /// Explicitly sets the output data format to use when converting.
        /// </summary>
        /// <param name="outDataFormat">The data format to convert the input to</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithOutputType(DataFormat outDataFormat)
        {
            ConverterSettings.OutDataFormat = outDataFormat;
            ConverterSettings.OutputTypeGiven = outDataFormat;
            return this;
        }

        /// <summary>
        /// Pretty-prints JSON, YAML, XML, or HTML. 
        /// Other formats (CSV, TAB, PIPE, etc.) are returned as-is.
        /// </summary>
        /// <summary>
        /// Pretty prints the data based on the CURRENT Input Format (InDataFormat).
        /// Works great with your fluent chain: WithInput(...).PrettyPrintTo(...)
        /// </summary>
        public IAllowConverterInputAndActions PrettyPrintTo(out string outputData)
        {
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClass = null;
            ConverterSettings.OutputClassType = null;
            ConverterSettings.OutputClassElementType = null;
            ConverterSettings.OutputGrid = null;

            // Use the Input format (already known from WithInput) for pretty printing
            if (ConverterSettings.InDataFormat.FirstOrDefault() != DataFormat.NOTSET)
                ConverterSettings.OutDataFormat = ConverterSettings.InDataFormat.FirstOrDefault();

            // a file given as the input is printed from its own text, its first file when it names several
            string inputText = ConverterSettings.InputData.FirstOrDefault();
            if (null != ConverterSettings.InputFile && 0 < ConverterSettings.InputFile.Filenames.Count)
            {
                if (true == File.Exists(ConverterSettings.InputFile.Filenames[0]))
                    inputText = File.ReadAllText(ConverterSettings.InputFile.Filenames[0]);
                else
                {
                    inputText = null;
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File missing: [{ConverterSettings.InputFile.Filenames[0]}]", this, GPALObjectType.Converter);
                }
            }

            outputData = FormatPretty(inputText);
            ConverterSettings.OutputData = outputData;

            return this;
        }


        /// <summary>
        /// Whether this payload should be written without YAML aliases.
        /// </summary>
        /// <remarks>
        /// Aliases let YAML carry a graph rather than a tree, and they do two different jobs. A cycle cannot be
        /// written without them at all: YamlDotNet follows the graph until it gives up and throws. A merely shared
        /// reference does not need them, it only comes out shorter and reads back as one object instead of two.
        /// <br/><br/>
        /// Turning them on costs a pre-pass over the whole graph to find repeat references, and on a large flat
        /// collection, tens of thousands of sitemap urls say, that pre-pass is what stalls Serialize while there is
        /// nothing in it worth anchoring. So they are dropped for a payload that is large and has no cycle, and
        /// kept for everything else. A cycle keeps them whatever its size, because the alternative is not a longer
        /// document, it is an exception.
        /// <br/><br/>
        /// The size is GPAL.WithYamlAliasLimit, 5000 unless a workflow says otherwise, and 0 keeps aliases always.
        /// </remarks>
        /// <param name="payload">The object about to be serialized</param>
        /// <returns>True to serialize without aliases</returns>
        bool DropAliasesFor(object payload)
        {
            int limit = GPAL.GPALSettings.YamlAliasLimit;
            bool retVal = false;

            if (0 < limit)
            {
                int nodes = ConverterHelper.CountGraphNodes(payload, limit, out bool cyclic);

                retVal = false == cyclic && nodes >= limit;

                if (true == cyclic && nodes >= limit)
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, $"The payload holds a cycle, so its aliases are kept whatever its size. Without them YAML cannot write it at all", null, Enums.GPALObjectType.Converter);
                else if (true == retVal)
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, $"The payload reached the alias limit of [{limit}] nodes and holds no cycle, so a shared reference is written out again rather than as an alias. GPAL.WithYamlAliasLimit moves the limit", null, Enums.GPALObjectType.Converter);
            }

            return retVal;
        }

        /// <summary>
        /// Pretty-prints the given input string according to the converter's current input data format
        /// (JSON, XML, HTML, or YAML get re-serialized with indentation; other formats are returned unchanged).
        /// Falls back to returning the input unchanged if pretty-printing fails for any reason.
        /// </summary>
        /// <param name="input">The raw input string to pretty-print</param>
        /// <returns>The pretty-printed string, or the original input if it could not be pretty-printed</returns>
        private string FormatPretty(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var format = ConverterSettings.InDataFormat.FirstOrDefault();

            try
            {
                switch (format)
                {
                    case DataFormat.JSON:
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(input);
                        return System.Text.Json.JsonSerializer.Serialize(jsonElement, jsonOptions);

                    case DataFormat.XML:
                        var xmlDoc = System.Xml.Linq.XDocument.Parse(input, System.Xml.Linq.LoadOptions.PreserveWhitespace);
                        return xmlDoc.ToString(System.Xml.Linq.SaveOptions.None);

                    case DataFormat.HTML:
                        // Prefer HtmlAgilityPack for real HTML, fallback to XDocument
                        try
                        {
                            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                            htmlDoc.LoadHtml(input);
                            return htmlDoc.DocumentNode.WriteContentTo(); // or .OuterHtml with formatting
                        }
                        catch
                        {
                            var xDoc = System.Xml.Linq.XDocument.Parse(input, System.Xml.Linq.LoadOptions.PreserveWhitespace);
                            return xDoc.ToString(System.Xml.Linq.SaveOptions.None);
                        }

                    case DataFormat.YAML:
                        // The documents are read as nodes, which keep the style each value was written in, so a quoted
                        // '007' stays quoted text and a plain 42 stays a number; anchors and aliases keep their names.
                        // Lists and mappings are written in block style, one entry to a line.
                        var yamlStream = new YamlStream();
                        yamlStream.Load(new StringReader(input));
                        foreach (YamlNode yamlNode in yamlStream.Documents.SelectMany(document => document.AllNodes))
                        {
                            if (yamlNode is YamlSequenceNode sequenceNode)
                                sequenceNode.Style = SequenceStyle.Block;
                            else if (yamlNode is YamlMappingNode mappingNode)
                                mappingNode.Style = MappingStyle.Block;
                        }
                        var serializer = new SerializerBuilder()
                            .WithMaximumRecursion(ConverterHelper.NestingLimit)
                            .WithIndentedSequences()
                            .Build();
                        return string.Join("---" + Environment.NewLine, yamlStream.Documents.Select(document => serializer.Serialize(document.RootNode)));

                    // These formats cannot/should not be pretty-printed
                    case DataFormat.CSV:
                    case DataFormat.TAB:
                    case DataFormat.LOG:    // a finished line of text, written and read as it stands
                    case DataFormat.PIPE:
                    case DataFormat.COLON:
                    case DataFormat.SEMICOLON:
                    case DataFormat.SPACE:
                    case DataFormat.HYPHEN:
                    case DataFormat.CARET:
                    case DataFormat.PRN:
                    case DataFormat.BASE64:
                    case DataFormat.DATABASE:
                    case DataFormat.XLSX:
                    //// post-v1: PDF case DataFormat.PDF:
                    case DataFormat.STRING:
                    case DataFormat.CLASS:
                    case DataFormat.DICTIONARY:
                    case DataFormat.DOT:
                    case DataFormat.GRID:
                        return input;

                    default:
                        return input;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"PrettyPrint failed for [{format}]", this, GPALObjectType.Converter, ex);
                return input; // safe fallback
            }
        }
    }
}
