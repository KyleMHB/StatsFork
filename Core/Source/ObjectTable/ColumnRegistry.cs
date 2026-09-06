using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Stats.ColumnWorkers;
using Stats.TableWorkers;

namespace Stats;

/// <summary>
/// Describes how a table session obtains one column worker. The session owns
/// the worker after creation and is responsible for releasing it.
/// </summary>
internal sealed class TableColumnDefinition<TObject>
{
    internal TableColumnDefinition(
        string defName,
        Func<ColumnWorker<TObject>> createWorker,
        Action<ICollection<CellField>, Action>? subscribe = null,
        Action<ICollection<CellField>, Action>? unsubscribe = null,
        Func<ColumnWorker<TObject>, ICollection<CellField>>? getCellFields = null)
    {
        DefName = string.IsNullOrWhiteSpace(defName)
            ? throw new ArgumentException("A column definition needs an identifier.", nameof(defName))
            : defName;
        CreateWorker = createWorker ?? throw new ArgumentNullException(nameof(createWorker));
        Subscribe = subscribe;
        Unsubscribe = unsubscribe;
        GetCellFields = getCellFields;
    }

    internal string DefName { get; }
    internal Func<ColumnWorker<TObject>> CreateWorker { get; }
    internal Action<ICollection<CellField>, Action>? Subscribe { get; }
    internal Action<ICollection<CellField>, Action>? Unsubscribe { get; }
    internal Func<ColumnWorker<TObject>, ICollection<CellField>>? GetCellFields { get; }
}

/// <summary>
/// Private implementation of the table column lifecycle. A definition has at
/// most one live worker, even while its column is hidden but has an active
/// filter. New workers are staged before the old registry is changed so a
/// failed construction leaves the last published state intact.
/// </summary>
internal sealed class ColumnRegistry<TObject> : IDisposable
{
    internal sealed class Entry
    {
        internal Entry(TableColumnDefinition<TObject> definition, ColumnWorker<TObject> worker)
        {
            Definition = definition;
            Worker = worker;
            Fields = definition.GetCellFields?.Invoke(worker) ?? Array.Empty<CellField>();
        }

        internal TableColumnDefinition<TObject> Definition { get; }
        internal ColumnWorker<TObject> Worker { get; }
        internal ICollection<CellField> Fields { get; }
        internal Action? FilterChanged { get; set; }
        internal bool IsDisposed { get; set; }
    }

    internal sealed class Transition : IDisposable
    {
        private readonly ColumnRegistry<TObject> _registry;
        private readonly Dictionary<string, Entry> _desired;
        private readonly List<Entry> _created;
        private Dictionary<string, Entry>? _previous;
        private IReadOnlyList<string>? _previousVisibleNames;
        private IReadOnlyList<string>? _previousFilterOnlyNames;
        private bool _committed;
        private bool _rolledBack;
        private bool _finalized;

        internal Transition(
            ColumnRegistry<TObject> registry,
            Dictionary<string, Entry> desired,
            List<Entry> created,
            IReadOnlyList<string> visibleNames,
            IReadOnlyList<string> filterOnlyNames)
        {
            _registry = registry;
            _desired = desired;
            _created = created;
            VisibleNames = visibleNames;
            FilterOnlyNames = filterOnlyNames;
        }

        internal IReadOnlyList<string> VisibleNames { get; }
        internal IReadOnlyList<string> FilterOnlyNames { get; }

        internal bool TryGetWorker(string defName, out ColumnWorker<TObject>? worker)
        {
            if (_desired.TryGetValue(defName, out Entry? entry))
            {
                worker = entry.Worker;
                return true;
            }

            worker = null;
            return false;
        }

        internal bool TryGetFields(string defName, out ICollection<CellField>? fields)
        {
            if (_desired.TryGetValue(defName, out Entry? entry))
            {
                fields = entry.Fields;
                return true;
            }

            fields = null;
            return false;
        }

        internal void Commit()
        {
            if (_committed || _rolledBack)
            {
                return;
            }

            _previous = new Dictionary<string, Entry>(_registry._entries, StringComparer.Ordinal);
            _previousVisibleNames = _registry._visibleNames;
            _previousFilterOnlyNames = _registry._filterOnlyNames;

            _registry._entries.Clear();
            foreach (KeyValuePair<string, Entry> desiredEntry in _desired)
            {
                _registry._entries.Add(desiredEntry.Key, desiredEntry.Value);
            }

            _registry._visibleNames = new ReadOnlyCollection<string>(VisibleNames.ToList());
            _registry._filterOnlyNames = new ReadOnlyCollection<string>(FilterOnlyNames.ToList());
            _committed = true;
        }

        internal void FinalizeCommit()
        {
            if (_committed == false || _finalized || _rolledBack)
            {
                return;
            }

            HashSet<Entry> desiredEntries = new(_desired.Values);
            try
            {
                foreach (Entry oldEntry in _previous!.Values)
                {
                    if (desiredEntries.Contains(oldEntry) == false)
                    {
                        _registry.Release(oldEntry);
                    }
                }
            }
            finally
            {
                _finalized = true;
            }
        }

        internal void Rollback()
        {
            if (_rolledBack || _finalized)
            {
                return;
            }

            if (_previous != null)
            {
                _registry._entries.Clear();
                foreach (KeyValuePair<string, Entry> previousEntry in _previous)
                {
                    _registry._entries.Add(previousEntry.Key, previousEntry.Value);
                }

                _registry._visibleNames = _previousVisibleNames!;
                _registry._filterOnlyNames = _previousFilterOnlyNames!;
            }

            _rolledBack = true;
            foreach (Entry entry in _created)
            {
                _registry.Release(entry);
            }
        }

        public void Dispose() => Rollback();
    }

    private readonly Dictionary<string, TableColumnDefinition<TObject>> _definitions;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Action _onFilterChanged;
    private IReadOnlyList<string> _visibleNames = Array.Empty<string>();
    private IReadOnlyList<string> _filterOnlyNames = Array.Empty<string>();
    private bool _disposed;

    internal ColumnRegistry(IEnumerable<TableColumnDefinition<TObject>> definitions, Action onFilterChanged)
    {
        if (definitions == null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        _definitions = new Dictionary<string, TableColumnDefinition<TObject>>(StringComparer.Ordinal);
        foreach (TableColumnDefinition<TObject> definition in definitions)
        {
            if (_definitions.ContainsKey(definition.DefName))
            {
                continue;
            }

            _definitions.Add(definition.DefName, definition);
        }

        _onFilterChanged = onFilterChanged ?? throw new ArgumentNullException(nameof(onFilterChanged));
    }

    internal IReadOnlyList<string> VisibleNames => _visibleNames;
    internal IReadOnlyList<string> FilterOnlyNames => _filterOnlyNames;
    internal IReadOnlyCollection<string> KnownNames => _definitions.Keys;

    internal bool TryGetWorker(string defName, out ColumnWorker<TObject>? worker)
    {
        if (_entries.TryGetValue(defName, out Entry? entry))
        {
            worker = entry.Worker;
            return true;
        }

        worker = null;
        return false;
    }

    internal bool TryGetFields(string defName, out ICollection<CellField>? fields)
    {
        if (_entries.TryGetValue(defName, out Entry? entry))
        {
            fields = entry.Fields;
            return true;
        }

        fields = null;
        return false;
    }

    internal Transition Stage(IEnumerable<string> visibleNames, IEnumerable<string> filterColumnNames)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ColumnRegistry<TObject>));
        }

        List<string> visible = NormalizeNames(visibleNames);
        List<string> filterOnly = NormalizeNames(filterColumnNames)
            .Where(name => visible.Contains(name, StringComparer.Ordinal) == false)
            .ToList();
        Dictionary<string, Entry> desired = new(StringComparer.Ordinal);
        List<Entry> created = [];
        try
        {
            foreach (string name in visible.Concat(filterOnly).Distinct(StringComparer.Ordinal))
            {
                if (_definitions.TryGetValue(name, out TableColumnDefinition<TObject>? definition) == false)
                {
                    continue;
                }

                Entry entry;
                if (_entries.TryGetValue(name, out Entry? existing))
                {
                    entry = existing;
                }
                else
                {
                    entry = CreateEntry(definition);
                    created.Add(entry);
                }

                desired.Add(name, entry);
            }

            return new Transition(this, desired, created, visible, filterOnly);
        }
        catch
        {
            foreach (Entry entry in created)
            {
                Release(entry);
            }

            throw;
        }
    }

    internal ColumnWorker<TObject> Acquire(string defName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ColumnRegistry<TObject>));
        }

        if (_entries.TryGetValue(defName, out Entry? existing))
        {
            return existing.Worker;
        }

        if (_definitions.TryGetValue(defName, out TableColumnDefinition<TObject>? definition) == false)
        {
            throw new KeyNotFoundException($"Column definition '{defName}' is not registered.");
        }

        Entry entry = CreateEntry(definition);
        _entries.Add(defName, entry);
        return entry.Worker;
    }

    internal void SetVisibleNames(IEnumerable<string> names)
    {
        Transition transition = Stage(names, Array.Empty<string>());
        try
        {
            transition.Commit();
            transition.FinalizeCommit();
        }
        finally
        {
            transition.Dispose();
        }
    }

    internal void ResetWorkers()
    {
        foreach (Entry entry in _entries.Values)
        {
            entry.Worker.ResetRows();
        }
    }

    private Entry CreateEntry(TableColumnDefinition<TObject> definition)
    {
        ColumnWorker<TObject> worker = definition.CreateWorker();
        Entry? entry = null;
        try
        {
            entry = new Entry(definition, worker);
            if (definition.Subscribe != null)
            {
                Action callback = _onFilterChanged;
                definition.Subscribe(entry.Fields, callback);
                entry.FilterChanged = callback;
            }

            return entry;
        }
        catch
        {
            if (entry != null)
            {
                Release(entry);
            }
            else
            {
                try
                {
                    worker.Dispose();
                }
                catch
                {
                    // Preserve the construction failure while containing
                    // cleanup failures from an external worker.
                }
            }
            throw;
        }
    }

    private void Release(Entry entry)
    {
        if (entry.IsDisposed)
        {
            return;
        }

        entry.IsDisposed = true;
        try
        {
            if (entry.FilterChanged != null && entry.Definition.Unsubscribe != null)
            {
                try
                {
                    entry.Definition.Unsubscribe(entry.Fields, entry.FilterChanged);
                }
                catch
                {
                    // A failed external unsubscription must not prevent the
                    // worker from reaching its idempotent disposal hook.
                }
            }
        }
        finally
        {
            try
            {
                entry.Worker.Dispose();
            }
            catch
            {
                // Worker cleanup is best effort and cannot invalidate a
                // committed registry transition.
            }
            entry.FilterChanged = null;
        }
    }

    private static List<string> NormalizeNames(IEnumerable<string> names)
    {
        return (names ?? Array.Empty<string>())
            .Where(name => string.IsNullOrWhiteSpace(name) == false)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Entry entry in _entries.Values.ToList())
        {
            Release(entry);
        }

        _entries.Clear();
        _visibleNames = Array.Empty<string>();
        _filterOnlyNames = Array.Empty<string>();
    }
}
