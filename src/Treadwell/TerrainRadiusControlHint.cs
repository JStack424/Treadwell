#nullable disable
using System;
using TMPro;
using UnityEngine;

namespace Treadwell
{
    internal sealed class TerrainRadiusControlHint
    {
        internal const string ObjectName = "TreadwellTerrainRadiusControlHint";
        internal const string ActionText = "Change Size";
        internal const string ShortcutText = "Alt + Scroll";

        private KeyHints _owner;
        private KeyHints _failedOwner;
        private GameObject _hint;

        internal void Update(KeyHints owner, bool visible)
        {
            if (owner == null)
            {
                Detach();
                return;
            }

            if (_hint != null && !ReferenceEquals(_owner, owner)) Detach();
            if (ReferenceEquals(_failedOwner, owner)) return;
            if (_failedOwner != null) _failedOwner = null;

            if (!visible)
            {
                if (_hint != null) _hint.SetActive(false);
                return;
            }

            if (_hint == null) Attach(owner);
            if (_hint != null) _hint.SetActive(true);
        }

        internal void FailForOwner(KeyHints owner)
        {
            try { Detach(); }
            finally { _failedOwner = owner; }
        }

        internal void Detach()
        {
            var owner = _owner;
            var hint = _hint;
            _owner = null;
            _failedOwner = null;
            _hint = null;

            Exception failure = null;
            try
            {
                if (hint != null) hint.SetActive(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                if (hint != null) UnityEngine.Object.Destroy(hint);
            }
            catch (Exception exception)
            {
                if (failure == null) failure = exception;
            }

            try
            {
                DestroyStaleOwnedHints(owner, hint);
            }
            catch (Exception exception)
            {
                if (failure == null) failure = exception;
            }

            if (failure != null) throw failure;
        }

        private void Attach(KeyHints owner)
        {
            Detach();
            DestroyStaleOwnedHints(owner, null);

            var buildHints = owner.m_buildHints;
            var templateLabel = owner.m_buildAlternativePlacingKey;
            var template = templateLabel != null ? templateLabel.gameObject : null;
            var parent = template != null ? template.transform.parent : null;
            if (buildHints == null || template == null || parent == null ||
                !BelongsToBuildHintRow(parent, buildHints.transform))
            {
                _failedOwner = owner;
                return;
            }

            var hint = (GameObject)UnityEngine.Object.Instantiate(
                (UnityEngine.Object)template, parent, false);
            _owner = owner;
            _hint = hint;

            var label = hint.GetComponent<TMP_Text>();
            if (label == null)
                throw new InvalidOperationException("the cloned vanilla placement hint contains no TMP text");
            Localization.instance?.RemoveTextFromCache(label);
            label.text = ShortcutText + " — " + ActionText;
            hint.name = ObjectName;
            hint.transform.SetAsLastSibling();
            hint.SetActive(false);
        }

        private static bool BelongsToBuildHintRow(Transform parent, Transform buildHintRoot)
        {
            for (var current = parent; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, buildHintRoot)) return true;
            }
            return false;
        }

        private static void DestroyStaleOwnedHints(KeyHints owner, GameObject retained)
        {
            if (owner == null || owner.m_buildHints == null) return;
            foreach (var child in owner.m_buildHints.GetComponentsInChildren<Transform>(true))
            {
                var candidate = child != null ? child.gameObject : null;
                if (candidate != null && !ReferenceEquals(candidate, retained) &&
                    string.Equals(candidate.name, ObjectName, StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(candidate);
                }
            }
        }
    }
}
