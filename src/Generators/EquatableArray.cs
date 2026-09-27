using System.Collections;
using System.Collections.Immutable;

namespace SourceGenerators.EF.Generators;

/// <summary>
/// ImmutableArray with value equality. Incremental pipeline values must be equatable
/// or every downstream step re-runs on each edit.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
	where T : IEquatable<T>
{
	private readonly ImmutableArray<T> _array = array;

	public int Count => _array.IsDefault ? 0 : _array.Length;

	public bool Equals(EquatableArray<T> other) => _array.AsSpan().SequenceEqual(other._array.AsSpan());
	public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

	public override int GetHashCode()
	{
		if (_array.IsDefault) return 0;
		var hash = 17;
		foreach (var item in _array) hash = unchecked(hash * 31 + item.GetHashCode());
		return hash;
	}

	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
