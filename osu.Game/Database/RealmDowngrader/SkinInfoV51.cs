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
    /// Mirror of <see cref="osu.Game.Skinning.SkinInfo"/> WITHOUT the
    /// <c>Pinned</c> column, used as the destination class when the v52
    /// realm is rebuilt at v51 for vanilla osu! lazer compatibility.
    ///
    /// Why a mirror is needed
    /// ----------------------
    /// Realm.NET derives the file's schema from the C# types in the
    /// <see cref="RealmConfiguration.Schema"/> list. Our normal
    /// <c>SkinInfo</c> declares the <c>Pinned</c> property, so any
    /// realm opened with our normal schema list ends up with that
    /// column in its file format — which is precisely what triggers
    /// the v51 → v52 schema bump and bricks vanilla. The downgrade
    /// runner builds a destination config that swaps <c>SkinInfo</c>
    /// for this class, so the v51 realm file is byte-for-byte
    /// compatible with vanilla's expected schema (no <c>Pinned</c>
    /// column).
    ///
    /// Both classes have <c>[MapTo("Skin")]</c> so the realm class
    /// name is the same — they're just different C# views over the
    /// same underlying realm-class slot. Only one can be in any
    /// given <see cref="RealmConfiguration.Schema"/> at a time.
    /// </summary>
    [MapTo("Skin")]
    public class SkinInfoV51 : RealmObject, IHasRealmFiles, IHasGuidPrimaryKey, ISoftDelete
    {
        [PrimaryKey]
        public Guid ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Creator { get; set; } = string.Empty;

        public string InstantiationInfo { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;

        public bool Protected { get; set; }

        public IList<RealmNamedFileUsage> Files { get; } = null!;

        public bool DeletePending { get; set; }

        [UsedImplicitly]
        public SkinInfoV51()
        {
        }

        IEnumerable<INamedFileUsage> IHasNamedFiles.Files => Files;
    }
}
