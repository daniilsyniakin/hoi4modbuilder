namespace HoI4ModBuilder.Clausewitz;

/// <summary>A half-open range <c>[Start, End)</c> of characters in the source text.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool IsEmpty => Length == 0;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    public override string ToString() => $"[{Start}..{End})";
}
