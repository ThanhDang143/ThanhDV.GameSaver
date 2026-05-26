using System;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ThanhDV.SaveKeeper.Editor
{
    /// <summary>
    /// UI Toolkit editor window for editing SaveKeeper settings. Reads/writes
    /// ProjectSettings/SaveKeeperSettings.json and triggers regeneration of the runtime factory
    /// in Assets/Plugins/SaveKeeper/.
    /// </summary>
    /// <remarks>
    /// Visual layout matches the previous IMGUI implementation pixel-for-pixel: centered title
    /// header, Storage and Auto Save sections, Apply/Reset row, Reload button, and a transient
    /// "Applied" banner. The migration to UI Toolkit was motivated by cleaner per-field event
    /// handling — notably, numeric-only KeyDown filtering attached directly to the numeric field
    /// instead of via a global focus-name lookup at the top of OnGUI.
    /// <para>
    /// Architecture:
    /// <list type="bullet">
    /// <item>Model fields (<c>_useEncryption</c>, <c>_fileName</c>, …) hold the editable draft.</item>
    /// <item>UI element references (<c>_useEncryptionToggle</c>, …) are kept so <see cref="Hydrate"/>
    /// can resync the form after a Reload/Reset without rebuilding the tree.</item>
    /// <item><see cref="BuildUI"/> runs once per <see cref="OnEnable"/>; <see cref="Hydrate"/>
    /// updates field values via <c>SetValueWithoutNotify</c> on subsequent loads.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public class SaveKeeperWindow : EditorWindow
    {
        // ----- Draft state (model) — mirrors SaveSettings as mutable fields because
        //       SaveSettings itself uses init-only setters.
        private bool _useEncryption;
        private string _fileName;
        private string _saveExtension;
        private string _metaExtension;
        private bool _enableAutoSave;
        private float _autoSaveTime;
        private bool _autoSaveOnQuit;
        private int _autoSaveOnQuitTimeout;

        // Snapshot of last on-disk state — used by IsDirty for the Apply button.
        private SaveSettings _lastApplied;

        // ----- UI element references kept so we can resync from Hydrate without rebuilding.
        private Toggle _useEncryptionToggle;
        private TextField _fileNameField;
        private TextField _saveExtensionField;
        private TextField _metaExtensionField;
        private Toggle _enableAutoSaveToggle;
        private FloatField _autoSaveTimeField;
        private Toggle _autoSaveOnQuitToggle;
        private IntegerField _quitTimeoutField;
        private Button _applyButton;
        private VisualElement _appliedFlash;
        private IVisualElementScheduledItem _flashHideTask;

        // Button tints — chosen darker/less saturated than the IMGUI version so the editor's
        // default light text reads cleanly against them in dark theme.
        private static readonly Color ApplyColor = new(0.35f, 0.55f, 0.35f);
        private static readonly Color ResetColor = new(0.65f, 0.35f, 0.35f);
        private static readonly Color SeparatorColor = new(0.5f, 0.5f, 0.5f, 1f);

        private const int FLASH_DURATION_MS = 2000;

        [MenuItem("Tools/ThanhDV/SaveKeeper/Settings")]
        private static void Open()
        {
            SaveKeeperWindow window = GetWindow<SaveKeeperWindow>(false, "SaveKeeper Settings", true);
            window.minSize = new Vector2(380, 380);
        }

        private void OnEnable()
        {
            LoadFromDisk();
            BuildUI();
        }

        #region UI construction

        private void BuildUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            ScrollView scroll = new();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 6;
            scroll.style.paddingRight = 6;
            root.Add(scroll);

            BuildHeader(scroll);
            BuildStorageSection(scroll);
            BuildAutoSaveSection(scroll);
            BuildFooter(scroll);
            BuildAppliedFlash(scroll);

            // Unify label widths across all BaseField<T> controls (Toggle, TextField, FloatField,
            // IntegerField) so their value columns line up. Without this, each label sizes to its
            // own text width and the inputs become a ragged column.
            scroll.Query<Label>(className: "unity-base-field__label").ForEach(label =>
            {
                label.style.minWidth = 150;
            });

            // Click on empty area inside the scroll view clears focus. Registering on the
            // ScrollView (rebuilt on every BuildUI) avoids leaking callbacks across multiple
            // OnEnable invocations the way registering on the persistent root would.
            scroll.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target is not VisualElement target) return;

                // Walk up from the clicked element; if no editable input is an ancestor, the
                // click landed on background space — drop focus from whatever was editing.
                VisualElement cursor = target;
                while (cursor != null)
                {
                    if (cursor is TextField || cursor is FloatField || cursor is IntegerField) return;
                    cursor = cursor.parent;
                }
                BlurFocused();
            });

            UpdateApplyEnabled();
        }

        private void BuildHeader(VisualElement parent)
        {
            VisualElement header = new();
            header.style.marginTop = 8;

            Label title = new("SaveKeeper - Settings");
            title.style.fontSize = 30;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.height = 50;
            header.Add(title);

            Label subtitle = new("Created by ThanhDV");
            subtitle.style.fontSize = 12;
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(subtitle);

            VisualElement separator = new();
            separator.style.height = 1;
            separator.style.backgroundColor = SeparatorColor;
            separator.style.marginTop = 10;
            separator.style.marginBottom = 10;
            header.Add(separator);

            parent.Add(header);
        }

        private void BuildStorageSection(VisualElement parent)
        {
            parent.Add(MakeSectionHeader("Storage"));

            _useEncryptionToggle = new Toggle("Use Encryption")
            {
                value = _useEncryption,
                tooltip = "If true, save files are encrypted using the IEncryptionProvider injected into SaveKeeper. The provider must be configured separately — this flag only gates whether the pipeline calls Encrypt/Decrypt.",
            };
            _useEncryptionToggle.RegisterValueChangedCallback(evt =>
            {
                _useEncryption = evt.newValue;
                UpdateApplyEnabled();
            });
            parent.Add(_useEncryptionToggle);

            _fileNameField = MakeTextField(
                "File Name",
                _fileName,
                "Base name for save files (without extension).",
                value => _fileName = value);
            parent.Add(_fileNameField);

            _saveExtensionField = MakeTextField(
                "Save Extension",
                _saveExtension,
                "File extension for save data (include the leading dot).",
                value => _saveExtension = value);
            parent.Add(_saveExtensionField);

            _metaExtensionField = MakeTextField(
                "Meta Extension",
                _metaExtension,
                "File extension for the metadata sidecar (include the leading dot).",
                value => _metaExtension = value);
            parent.Add(_metaExtensionField);
        }

        private void BuildAutoSaveSection(VisualElement parent)
        {
            parent.Add(MakeSectionHeader("Auto Save"));

            _enableAutoSaveToggle = new Toggle("Enable Auto Save")
            {
                value = _enableAutoSave,
                tooltip = "If true, SaveKeeper auto-saves on a timer while a profile is active.",
            };
            _enableAutoSaveToggle.RegisterValueChangedCallback(evt =>
            {
                _enableAutoSave = evt.newValue;
                _autoSaveTimeField.SetEnabled(evt.newValue);
                UpdateApplyEnabled();
            });
            parent.Add(_enableAutoSaveToggle);

            _autoSaveTimeField = new FloatField("Auto Save Interval (s)")
            {
                value = _autoSaveTime,
                tooltip = "Seconds between auto-saves when a profile is active.",
            };
            _autoSaveTimeField.SetEnabled(_enableAutoSave);
            _autoSaveTimeField.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Max(0f, evt.newValue);
                _autoSaveTime = clamped;
                if (!Mathf.Approximately(clamped, evt.newValue))
                {
                    _autoSaveTimeField.SetValueWithoutNotify(clamped);
                }
                UpdateApplyEnabled();
            });
            AttachNumericKeyFilter(_autoSaveTimeField, allowDecimal: true);
            parent.Add(_autoSaveTimeField);

            _autoSaveOnQuitToggle = new Toggle("Auto Save On Quit")
            {
                value = _autoSaveOnQuit,
                tooltip = "Automatically drain pending async saves and call SaveImmediate when the application is quitting or paused on mobile.",
            };
            _autoSaveOnQuitToggle.RegisterValueChangedCallback(evt =>
            {
                _autoSaveOnQuit = evt.newValue;
                _quitTimeoutField.SetEnabled(evt.newValue);
                UpdateApplyEnabled();
            });
            parent.Add(_autoSaveOnQuitToggle);

            _quitTimeoutField = new IntegerField("Quit Timeout (ms)")
            {
                value = _autoSaveOnQuitTimeout,
                tooltip = "Maximum time (milliseconds) to wait for in-flight async saves to drain on quit. After timeout, SaveImmediate is forced anyway as best-effort.",
            };
            _quitTimeoutField.SetEnabled(_autoSaveOnQuit);
            _quitTimeoutField.RegisterValueChangedCallback(evt =>
            {
                int clamped = Mathf.Max(0, evt.newValue);
                _autoSaveOnQuitTimeout = clamped;
                if (clamped != evt.newValue)
                {
                    _quitTimeoutField.SetValueWithoutNotify(clamped);
                }
                UpdateApplyEnabled();
            });
            AttachNumericKeyFilter(_quitTimeoutField, allowDecimal: false);
            parent.Add(_quitTimeoutField);
        }

        private void BuildFooter(VisualElement parent)
        {
            VisualElement spacerTop = new();
            spacerTop.style.height = 8;
            parent.Add(spacerTop);

            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;

            _applyButton = new Button(() =>
            {
                BlurFocused();
                Apply();
            })
            {
                text = "Apply",
            };
            _applyButton.style.flexGrow = 1;
            _applyButton.style.height = 28;
            _applyButton.style.backgroundColor = ApplyColor;
            row.Add(_applyButton);

            Button reset = new(() =>
            {
                BlurFocused();
                ResetToDefaults();
            })
            {
                text = "Reset to Defaults",
            };
            reset.style.width = 140;
            reset.style.height = 28;
            reset.style.backgroundColor = ResetColor;
            row.Add(reset);

            parent.Add(row);

            VisualElement spacerBetween = new();
            spacerBetween.style.height = 8;
            parent.Add(spacerBetween);

            Button reload = new(() =>
            {
                BlurFocused();
                LoadFromDisk();
            })
            {
                text = "Reload from Disk",
            };
            parent.Add(reload);
        }

        private void BuildAppliedFlash(VisualElement parent)
        {
            _appliedFlash = new HelpBox(
                "Applied — settings saved to ProjectSettings/SaveKeeperSettings.json and SaveKeeperSettings.cs regenerated.",
                HelpBoxMessageType.Info);
            _appliedFlash.style.display = DisplayStyle.None;
            _appliedFlash.style.marginTop = 8;
            parent.Add(_appliedFlash);
        }

        private static Label MakeSectionHeader(string text)
        {
            Label label = new(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            return label;
        }

        /// <summary>
        /// Creates a TextField bound to the given setter, with tooltip. Used for the three
        /// path/extension fields in the Storage section.
        /// </summary>
        private TextField MakeTextField(string label, string initial, string tooltip, Action<string> setter)
        {
            TextField field = new(label)
            {
                value = initial ?? string.Empty,
                tooltip = tooltip,
            };
            field.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                UpdateApplyEnabled();
            });
            return field;
        }

        /// <summary>
        /// Registers a TrickleDown KeyDown handler that drops any printable character that is
        /// not a digit (or a decimal point when <paramref name="allowDecimal"/> is true). The
        /// TrickleDown phase ensures we intercept the event before the field's internal text
        /// input processes it, so invalid characters never enter the buffer.
        /// </summary>
        private static void AttachNumericKeyFilter(VisualElement field, bool allowDecimal)
        {
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                char c = evt.character;
                if (c == '\0') return; // navigation key (arrows, Home, End, F-keys, modifiers)
                if (char.IsDigit(c)) return;
                if (c == '\b' || c == '\t' || c == '\n' || c == '\r') return; // editing control chars
                if (allowDecimal && c == '.') return;
                evt.StopPropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
        }

        #endregion

        #region Actions

        private void LoadFromDisk()
        {
            SaveSettings loaded = SaveSettingsIO.Load();
            Hydrate(loaded);
            _lastApplied = loaded;
        }

        private void Apply()
        {
            try
            {
                SaveSettings snapshot = BuildSnapshot();
                SaveSettingsIO.Save(snapshot);
                SaveKeeperSettingsGenerator.Generate(snapshot);
                _lastApplied = snapshot;
                ShowAppliedFlash();
                DebugLog.Success("SaveKeeper settings applied.");
            }
            catch (Exception e)
            {
                DebugLog.Error($"Failed to apply SaveKeeper settings: {e.Message}");
                EditorUtility.DisplayDialog("SaveKeeper", $"Failed to apply settings:\n\n{e.Message}", "OK");
            }
            UpdateApplyEnabled();
        }

        private void ResetToDefaults()
        {
            Hydrate(new SaveSettings());
        }

        private void ShowAppliedFlash()
        {
            _appliedFlash.style.display = DisplayStyle.Flex;
            _flashHideTask?.Pause();
            _flashHideTask = _appliedFlash.schedule.Execute(() =>
            {
                _appliedFlash.style.display = DisplayStyle.None;
            }).StartingIn(FLASH_DURATION_MS);
        }

        private void BlurFocused()
        {
            if (rootVisualElement.focusController?.focusedElement is VisualElement focused)
            {
                focused.Blur();
            }
        }

        #endregion

        #region State sync

        private void UpdateApplyEnabled()
        {
            if (_applyButton == null) return;
            bool needsInitialApply = !SaveSettingsIO.Exists;
            _applyButton.SetEnabled(needsInitialApply || IsDirty());
        }

        private bool IsDirty()
        {
            if (_lastApplied == null) return true;
            return _useEncryption != _lastApplied.UseEncryption
                   || _fileName != _lastApplied.FileName
                   || _saveExtension != _lastApplied.SaveExtension
                   || _metaExtension != _lastApplied.MetaExtension
                   || _enableAutoSave != _lastApplied.EnableAutoSave
                   || !Mathf.Approximately(_autoSaveTime, _lastApplied.AutoSaveTime)
                   || _autoSaveOnQuit != _lastApplied.AutoSaveOnQuit
                   || _autoSaveOnQuitTimeout != _lastApplied.AutoSaveOnQuitTimeout;
        }

        /// <summary>
        /// Copies values from the given <see cref="SaveSettings"/> into the local draft fields and,
        /// if the UI has already been built, into the corresponding UI elements via
        /// <c>SetValueWithoutNotify</c> (so the value-changed callbacks don't fire).
        /// </summary>
        private void Hydrate(SaveSettings s)
        {
            _useEncryption = s.UseEncryption;
            _fileName = s.FileName;
            _saveExtension = s.SaveExtension;
            _metaExtension = s.MetaExtension;
            _enableAutoSave = s.EnableAutoSave;
            _autoSaveTime = s.AutoSaveTime;
            _autoSaveOnQuit = s.AutoSaveOnQuit;
            _autoSaveOnQuitTimeout = s.AutoSaveOnQuitTimeout;

            // Sync UI if it's already been built (LoadFromDisk on Reload, ResetToDefaults).
            if (_useEncryptionToggle == null) return;

            _useEncryptionToggle.SetValueWithoutNotify(_useEncryption);
            _fileNameField.SetValueWithoutNotify(_fileName ?? string.Empty);
            _saveExtensionField.SetValueWithoutNotify(_saveExtension ?? string.Empty);
            _metaExtensionField.SetValueWithoutNotify(_metaExtension ?? string.Empty);
            _enableAutoSaveToggle.SetValueWithoutNotify(_enableAutoSave);
            _autoSaveTimeField.SetValueWithoutNotify(_autoSaveTime);
            _autoSaveTimeField.SetEnabled(_enableAutoSave);
            _autoSaveOnQuitToggle.SetValueWithoutNotify(_autoSaveOnQuit);
            _quitTimeoutField.SetValueWithoutNotify(_autoSaveOnQuitTimeout);
            _quitTimeoutField.SetEnabled(_autoSaveOnQuit);
            UpdateApplyEnabled();
        }

        private SaveSettings BuildSnapshot() => new()
        {
            UseEncryption = _useEncryption,
            FileName = _fileName,
            SaveExtension = _saveExtension,
            MetaExtension = _metaExtension,
            EnableAutoSave = _enableAutoSave,
            AutoSaveTime = _autoSaveTime,
            AutoSaveOnQuit = _autoSaveOnQuit,
            AutoSaveOnQuitTimeout = _autoSaveOnQuitTimeout,
        };

        #endregion
    }
}
