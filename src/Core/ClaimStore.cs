using System;
using System.Collections.Generic;

namespace Verboten.Core
{
    /// <summary>
    /// The single owner of all claims. All changes go through here (main thread only).
    /// Readers use <see cref="Index"/>, which is an immutable snapshot swapped atomically.
    /// Claims inside the index are clones, so readers never see half-applied changes.
    /// </summary>
    public sealed class ClaimStore
    {
        private readonly List<Claim> _claims = new List<Claim>();
        private volatile ClaimIndex _index = ClaimIndex.Empty;
        private int _version;

        /// <summary>Raised after every change (and after Load/Clear).</summary>
        public event Action Changed;

        public ClaimIndex Index => _index;
        public bool Dirty { get; private set; }

        public IReadOnlyList<Claim> Snapshot() => _index.Claims;

        public void Clear()
        {
            _claims.Clear();
            Publish(dirty: false);
        }

        /// <summary>Replace everything (after loading a save).</summary>
        public void Load(IEnumerable<Claim> claims)
        {
            _claims.Clear();
            foreach (var c in claims)
            {
                if (c == null) continue;
                if (c.Members == null) c.Members = new List<ClaimMember>();
                if (c.Incidents == null) c.Incidents = new List<Incident>();
                if (c.Fields == null) c.Fields = new List<ClaimField>();
                if (c.Donations == null) c.Donations = new List<Donation>();
                c.UpdateBounds(); // field claims: the rectangle always follows the fields
                if (string.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString("N");
                c.Upgrade();
                _claims.Add(c);
            }
            Publish(dirty: false);
        }

        public void Add(Claim claim)
        {
            _claims.Add(claim);
            Publish(dirty: true);
        }

        public bool Remove(string id)
        {
            int i = _claims.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            _claims.RemoveAt(i);
            Publish(dirty: true);
            return true;
        }

        /// <summary>Change a claim through a callback, then republish. Returns false if not found.</summary>
        public bool Update(string id, Action<Claim> change)
        {
            var claim = _claims.Find(c => c.Id == id);
            if (claim == null) return false;
            change(claim);
            Publish(dirty: true);
            return true;
        }

        /// <summary>
        /// Several changes at once (e.g. cut back abandoned claims and add a new one), published once.
        /// The callback gets the live list; return false to discard nothing but skip publishing.
        /// </summary>
        public void Batch(Func<List<Claim>, bool> change)
        {
            if (change(_claims)) Publish(dirty: true);
        }

        /// <summary>Deep copies of the live claims, for saving.</summary>
        public List<Claim> CopyForSave()
        {
            var list = new List<Claim>(_claims.Count);
            foreach (var c in _claims) list.Add(c.Clone());
            Dirty = false;
            return list;
        }

        private void Publish(bool dirty)
        {
            var clones = new List<Claim>(_claims.Count);
            foreach (var c in _claims) clones.Add(c.Clone());
            _index = new ClaimIndex(clones, ++_version);
            if (dirty) Dirty = true;
            Changed?.Invoke();
        }
    }
}
