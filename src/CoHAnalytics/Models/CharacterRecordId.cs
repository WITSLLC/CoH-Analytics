namespace CoHAnalytics.Models;

/// <summary>
/// Stable opaque identity for one logical character record in the Character Repository.
/// </summary>
/// <remarks>
/// Product and architecture documents refer to this durable key as <c>CharacterStableId</c>.
/// This type is the implementation of that concept; do not introduce a second identity id.
/// Never derived from a character name, account folder path, or any parsed chat content.
/// Names are display and matching properties; this id is the durable identity anchor.
/// </remarks>
public sealed record CharacterRecordId
{
    private CharacterRecordId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CharacterRecordId CreateNew() => new(Guid.NewGuid());

    public static CharacterRecordId FromGuid(Guid value) => new(value);

    public override string ToString() => Value.ToString("n");
}
