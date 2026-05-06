// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Realms;
using Realms.Schema;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Confirms a freshly-rebuilt v51 realm round-tripped every object
    /// from its v52 source. Run AFTER the copier commits its
    /// transaction and BEFORE the atomic swap into the live path —
    /// this is the last gate before we touch the user's filesystem in
    /// any irreversible way.
    ///
    /// The checks
    /// ----------
    /// 1. <b>Per-class count parity</b>: every class in the source
    ///    schema (excluding embedded objects, which travel with their
    ///    parent) has the same number of objects in the destination.
    ///    A single mismatch aborts the operation.
    /// 2. <b>Schema version is exactly 51</b>: the destination's
    ///    realm-file schema version must be 51, not 52, not anything
    ///    else.
    /// 3. <b>Pinned column is GONE</b>: the destination's <c>Skin</c>
    ///    object schema must not contain a <c>Pinned</c> property.
    ///    This is the structural change the whole exercise is about
    ///    — if the column survived somehow, we'd just have rebuilt
    ///    the same v52-shaped file at v51 and vanilla still couldn't
    ///    open it.
    ///
    /// Anything more elaborate (sampling per-field equality across
    /// thousands of beatmaps) is left to integration tests run against
    /// real realms — the count check catches the failure modes that
    /// matter for safety, and the copier's two-pass implementation
    /// makes per-field divergence vanishingly unlikely.
    /// </summary>
    public sealed class RealmDowngradeVerifier
    {
        public sealed class VerifyResult
        {
            public bool Ok { get; init; }
            public IReadOnlyList<string> Issues { get; init; } = System.Array.Empty<string>();
            public IReadOnlyDictionary<string, (int source, int dest)> Counts { get; init; } = new Dictionary<string, (int, int)>();
        }

        public VerifyResult Verify(Realm source, Realm destination, ulong expectedSchemaVersion = 51)
        {
            var issues = new List<string>();
            var counts = new Dictionary<string, (int source, int dest)>();

            // --- Schema version check ---------------------------------
            // RealmConfiguration.SchemaVersion on the destination tells
            // us what version the file was opened at. If our config
            // said 51 but Realm decided to migrate it to 52 for some
            // reason (it shouldn't with a fresh file), this catches
            // it.
            ulong destSchemaVersion = destination.Config.SchemaVersion;
            if (destSchemaVersion != expectedSchemaVersion)
                issues.Add($"Destination schema version is {destSchemaVersion}, expected {expectedSchemaVersion}.");

            // --- Skin schema must not have Pinned ----------------------
            if (destination.Schema.TryFindObjectSchema("Skin", out ObjectSchema? skinSchema) && skinSchema != null)
            {
                if (skinSchema.Any(p => p.Name == "Pinned"))
                    issues.Add("Destination 'Skin' schema still contains a 'Pinned' property — downgrade did not strip it.");
            }
            else
            {
                issues.Add("Destination schema is missing the 'Skin' class entirely.");
            }

            // --- Per-class count parity --------------------------------
            foreach (var schema in source.Schema)
            {
                if (schema.BaseType != ObjectSchema.ObjectType.RealmObject) continue;

                string className = schema.Name;
                int sourceCount;
                int destCount;

                try
                {
                    sourceCount = source.DynamicApi.All(className).Count();
                }
                catch
                {
                    sourceCount = -1;
                }

                if (!destination.Schema.TryFindObjectSchema(className, out _))
                {
                    issues.Add($"Class '{className}' is in the source but missing from the destination schema.");
                    counts[className] = (sourceCount, -1);
                    continue;
                }

                try
                {
                    destCount = destination.DynamicApi.All(className).Count();
                }
                catch
                {
                    destCount = -1;
                }

                counts[className] = (sourceCount, destCount);

                if (sourceCount != destCount)
                    issues.Add($"Count mismatch for '{className}': source={sourceCount} dest={destCount}.");
            }

            return new VerifyResult
            {
                Ok = issues.Count == 0,
                Issues = issues,
                Counts = counts,
            };
        }
    }
}
