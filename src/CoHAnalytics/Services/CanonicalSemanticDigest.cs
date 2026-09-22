using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Determinism canary over ordered semantic canonical fields. Length-prefixed encoding keeps
/// field boundaries unambiguous. Runtime identity, observation, byte-offset, and sequence fields
/// are excluded so independent rereads of the same fixture can match. Raw log text is excluded.
/// </summary>
public static class CanonicalSemanticDigest
{
    public static string Hash(IReadOnlyList<CanonicalCombatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoder = new Encoder(hash);
        encoder.Int32(events.Count);
        foreach (var item in events)
        {
            encoder.Int32(item.Provenance.GrammarSetVersion);
            encoder.Enum(item.Family);
            encoder.Enum(item.GrammarId);
            AppendActor(encoder, item.Actor);
            encoder.Nullable(item.Target, target => AppendActor(encoder, target));
            encoder.String(item.PowerName);
            encoder.Int64(item.Amount.Hundredths);
            encoder.Enum(item.Magnitude);
            encoder.Boolean(item.DamageType.HasValue);
            if (item.DamageType is { } type)
            {
                encoder.String(type.Text);
                encoder.Boolean(type.IsUnresistable);
                encoder.Boolean(type.IsUnique);
            }
            encoder.Enum(item.Delivery);
            encoder.String(item.EffectSuffix);
            encoder.NullableEnum(item.Outcome);
            encoder.NullableInt64(item.DisplayedChanceHundredths);
            encoder.NullableInt64(item.RollHundredths);
            encoder.String(item.SourceChannel);
            encoder.Enum(item.MirrorClass.Family);
            encoder.Enum(item.MirrorClass.GrammarId);
            encoder.String(item.MirrorClass.SourceChannel);
            encoder.Enum(item.Facets);
            encoder.String(item.StatusName);
            encoder.NullableEnum(item.PowerStateTransition);
            encoder.NullableInt64(item.SourceTimestamp?.Ticks);
            encoder.Boolean(item.DuplicateOf is not null);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendActor(Encoder encoder, ActorRef actor)
    {
        encoder.Enum(actor.Type);
        encoder.String(actor.DisplayName);
        encoder.Nullable(actor.PetKey, pet =>
        {
            encoder.String(pet.NormalizedPetName);
            encoder.String(pet.OwnerRecordId);
            encoder.NullableInt32(pet.InstanceOrdinal);
            encoder.Boolean(pet.CoverageLimited);
        });
    }

    private sealed class Encoder(IncrementalHash hash)
    {
        private readonly byte[] _number = new byte[8];

        public void Boolean(bool value) => hash.AppendData([value ? (byte)1 : (byte)0]);

        public void Enum<T>(T value) where T : struct, Enum => Int64(Convert.ToInt64(value));

        public void Int32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_number, value);
            hash.AppendData(_number.AsSpan(0, sizeof(int)));
        }

        public void Int64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_number, value);
            hash.AppendData(_number);
        }

        public void Nullable<T>(T? value, Action<T> append) where T : class
        {
            Boolean(value is not null);
            if (value is not null)
            {
                append(value);
            }
        }

        public void NullableEnum<T>(T? value) where T : struct, Enum
        {
            Boolean(value.HasValue);
            if (value.HasValue)
            {
                Enum(value.Value);
            }
        }

        public void NullableInt32(int? value)
        {
            Boolean(value.HasValue);
            if (value.HasValue)
            {
                Int32(value.Value);
            }
        }

        public void NullableInt64(long? value)
        {
            Boolean(value.HasValue);
            if (value.HasValue)
            {
                Int64(value.Value);
            }
        }

        public void String(string? value)
        {
            if (value is null)
            {
                Int32(-1);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            Int32(bytes.Length);
            hash.AppendData(bytes);
        }
    }
}
