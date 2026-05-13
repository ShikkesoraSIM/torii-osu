// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using osu.Game.Models;
using Realms;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Source-side mirror of <see cref="osu.Game.Skinning.SkinInfo"/>
    /// frozen at the v52 shape (i.e. WITH the <c>Pinned</c> column),
    /// used by the downgrade runner to open users' existing v52 realm
    /// files. The production <c>SkinInfo</c> no longer carries
    /// <c>Pinned</c> (pin state moved to <see cref="osu.Game.Skinning.PinnedSkinsStore"/>),
    /// so without this mirror Realm would reject a v52 file as having
    /// an "extra column not in schema" the moment we tried to open it
    /// to read pre-existing pinned values out.
    ///
    /// Both <see cref="SkinInfoV51"/> and this class use
    /// <c>[MapTo("Skin")]</c> so they occupy the same realm-class slot;
    /// only one can appear in any given <see cref="RealmConfiguration.Schema"/>.
    /// The runner keeps them in separate configs (this one for the
    /// source, V51 for the destination).
    /// </summary>
    [MapTo("Skin")]
    public class SkinInfoV52 : RealmObject, IHasRealmFiles, IHasGuidPrimaryKey, ISoftDelete
    {
        [PrimaryKey]
        public Guid ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Creator { get; set; } = string.Empty;

        public string InstantiationInfo { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;

        public bool Protected { get; set; }

        public bool Pinned { get; set; }

        public IList<RealmNamedFileUsage> Files { get; } = null!;

        public bool DeletePending { get; set; }

        [UsedImplicitly]
        public SkinInfoV52()
        {
        }

        IEnumerable<INamedFileUsage> IHasNamedFiles.Files => Files;
    }
}
