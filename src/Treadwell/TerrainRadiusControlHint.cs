#nullable disable
using System;
using TMPro;
using Treadwell.Core;
using UnityEngine;
using UnityEngine.UI;

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
            var buildRoot = buildHints != null ? buildHints.transform : null;
            var inputHint = buildHints != null ? buildHints.GetComponent<UIInputHint>() : null;
            var gamepad = inputHint != null ? inputHint.m_gamepadHint : null;
            var keyboard = inputHint != null ? inputHint.m_mouseKeyboardHint : null;
            var gamepadTransform = gamepad != null ? gamepad.transform : null;
            var keyboardTransform = keyboard != null ? keyboard.transform : null;
            var templateTransform = template != null ? template.transform : null;
            var sourceLayout = template != null ? template.GetComponent<LayoutElement>() : null;

            // Valheim's m_buildAlternativePlacingKey is a Gamepad child. Clone its
            // simple TMP/LayoutElement shape into the sibling Keyboard layout so
            // the owned hint is in the branch that is actually active for Alt-wheel.
            var compatible = buildRoot != null && templateTransform != null &&
                             gamepadTransform != null && keyboardTransform != null &&
                             TerrainRadiusHintLayoutRouting.CanAttach(
                                 !ReferenceEquals(gamepadTransform, keyboardTransform),
                                 ReferenceEquals(gamepadTransform.parent, buildRoot),
                                 ReferenceEquals(keyboardTransform.parent, buildRoot),
                                 ReferenceEquals(templateTransform.parent, gamepadTransform),
                                 gamepad.GetComponent<HorizontalLayoutGroup>() != null,
                                 keyboard.GetComponent<HorizontalLayoutGroup>() != null,
                                 sourceLayout != null && !sourceLayout.ignoreLayout);
            if (!compatible)
            {
                _failedOwner = owner;
                return;
            }

            var hint = (GameObject)UnityEngine.Object.Instantiate(
                (UnityEngine.Object)template, keyboardTransform, false);
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
