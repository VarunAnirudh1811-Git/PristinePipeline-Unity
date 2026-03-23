# Contributing to Pristine Pipeline

Pristine Pipeline is an open source project and contributions are welcome! Whether you want to fix a bug, suggest a feature, or explore the codebase — this document is for you.

---

## 🗂️ Project Structure

```
Packages/com.glyphlabs.pristinepipeline/
  Editor/
    Core/                  ← Main window, settings, and profile registry
    FolderGenerator/       ← Folder Generator tab and utilities
    AssetOrganizer/        ← Asset Organizer tab, processor, and utilities
    FBXImporter/           ← FBX Importer tab, processor, and utilities
  Runtime/
    Data/                  ← ScriptableObject data types (templates and profiles)
  package.json             ← UPM package manifest
```

**Runtime vs Editor:** Data assets (`FolderTemplate`, `AssetMappingProfile`, `FBXImportProfile`) live in `Runtime/` so they are accessible as ScriptableObjects across the project. All tool logic lives in `Editor/` and is stripped from builds.

---

## Architecture Overview

Pristine Pipeline follows a strict separation of concerns. Every tool is split into three layers:

- **Tab** — UI only. Owns the EditorWindow drawing logic and mode transitions. Never touches the filesystem or AssetDatabase directly.
- **Utility** — Stateless logic only. All file I/O, AssetDatabase calls, and rule matching live here. Safe to call from any editor context, including processors.
- **Processor** — Unity's asset pipeline hook. Listens to import events via `AssetPostprocessor` and delegates all logic to the Utility class.

This means if you want to change how assets are moved or how FBX settings are applied, you only ever touch the Utility class — not the Tab or the Processor.

---

## Core Systems

### ToolSettings.cs

The single source of truth for all persisted settings. Wraps `EditorPrefs` with typed properties and namespaced keys.

- **No tool should ever call `EditorPrefs` directly** — always go through `ToolSettings`
- All keys are prefixed with `ToolInfo.SettingsPrefix` to avoid collisions with other Unity tools
- `ResetAll()` wipes every key at once — used by the Settings tab reset button
- Adding a new setting = add a private key constant, a public property with get/set, and a `DeleteKey` call in `ResetAll()`

```csharp
// Correct
bool enabled = ToolSettings.Organizer_Enabled;

// Wrong — never do this
bool enabled = EditorPrefs.GetBool("Organizer.Enabled");
```

---

### ProfileRegistry.cs

Sits between `ToolSettings` (which stores GUIDs) and the rest of the codebase (which needs actual asset references). Resolves GUIDs to ScriptableObject instances.

- **No tool should ever call `AssetDatabase.GUIDToAssetPath` for profile resolution** — always go through `ProfileRegistry`
- Uses a generic `Load<T>` / `Save<T>` pair internally — adding a new tool only requires adding a new pair of convenience methods at the bottom
- Returns `null` cleanly if the GUID is empty or the asset has been deleted or moved

```csharp
// Correct
FBXImportProfile profile = ProfileRegistry.GetActiveImportProfile();

// Wrong — never do this
string path = AssetDatabase.GUIDToAssetPath(ToolSettings.FBX_ActiveProfileGuid);
FBXImportProfile profile = AssetDatabase.LoadAssetAtPath<FBXImportProfile>(path);
```

---

### ToolInfo.cs

Holds all static metadata about the package — tool name, version, author, menu paths, log prefix, and default save paths. If you need to reference anything about the package identity, it lives here.

---

## Data Models

All data assets are `ScriptableObject` subclasses that live in `Runtime/Data/`. They are created via **Right Click > Create > GlyphLabs** in the Project window.

### FolderTemplate
Stores a list of folder path strings. Exposes a read-only `IReadOnlyList<string>` via `FolderPaths`. The list is modified only through `SetFolderPaths()`, `AddFolderPath()`, and `RemoveFolderPathAt()` to keep the serialized state consistent.

- `isBuiltIn` is set only on package-bundled templates and is hidden from the Inspector
- When `isBuiltIn` is true, the Folder Generator tab disables Edit and Delete buttons for that template

### AssetMappingProfile
Stores a list of `MappingRule` objects. Each rule holds an extension, an optional wildcard name pattern, and a destination folder path.

- Extensions are stored **without** the leading dot (e.g. `png` not `.png`) for consistent comparison
- `HasNamePattern` is a convenience property — true when `namePattern` is not null or whitespace
- Rules are evaluated in order — **last match wins** — giving the user explicit control over priority by reordering

### FBXImportProfile
Stores a list of `FBXImportRule` objects, a default `FBXImportPreset`, a naming convention toggle, and a list of valid prefixes.

- Each `FBXImportRule` contains its own embedded `FBXImportPreset` rather than referencing a shared one — this keeps each rule self-contained and avoids shared-state bugs
- Rules are also evaluated in order with **last match wins**, consistent with the Asset Organizer
- `defaultPreset` is applied with a warning when no rule matches — it is never silently applied

---

## Rule Matching

Both the Asset Organizer and FBX Importer use the same wildcard matching strategy:

- Supports `*` (any sequence of characters) and `?` (any single character)
- Case-insensitive
- Implemented with a dynamic programming approach in `MatchesWildcard()` inside each Utility class
- **Last match wins** — all rules are evaluated in order, every matching rule updates the result, so rules lower in the list take priority

If you ever need to upgrade wildcard matching to full regex, replace only the body of `MatchesWildcard()` — nothing else in the system needs to change.

---

## Adding a New Tool

The shell is designed so that adding a new tool requires minimal changes outside its own files:

1. Add a new `TabID` constant and a label string in `PristinePipelineEditor.cs`
2. Add a `case` for it in `DrawActiveTab()`
3. Create a `MyToolTab.cs` in `Editor/MyTool/` — owns UI and mode state only
4. Create a `MyToolUtility.cs` in `Editor/MyTool/` — owns all asset operations
5. Create a `MyToolProcessor.cs` if the tool needs to hook into Unity's import pipeline
6. Add settings entries in `ToolSettings.cs` and convenience accessors in `ProfileRegistry.cs`
7. If the tool uses a data asset, create a `ScriptableObject` subclass in `Runtime/Data/`

Nothing in `Core/` needs to change beyond steps 1 and 2.

---

## FBX Import Pipeline Hook

`FBXImporterProcessor` extends `AssetPostprocessor` and overrides `OnPreprocessModel()`.

- `OnPreprocessModel` fires **before** Unity processes the model — this is the correct hook for overriding `ModelImporter` settings programmatically
- `OnPostprocessModel` fires **after** — too late to change settings, do not use it for this purpose
- The processor bails early if the file is not an `.fbx`, if the importer is disabled, or if no active profile is set
- Naming convention enforcement uses `throw new System.Exception(...)` to block the import — Unity catches this and aborts the import pass cleanly

---

## Profile Import / Export

All three tools support JSON import and export using `JsonUtility`. Each Utility class has a matching `[Serializable]` data container class (e.g. `FolderTemplateData`, `FBXImportProfileData`) used only for serialization — these are never used at runtime.

- Export opens a save panel via `EditorUtility.SaveFilePanel`
- Import opens an open panel via `EditorUtility.OpenFilePanel`
- On import, the asset is always saved to the configured save path, never to the path it was loaded from
- Duplicate names are handled by `BuildUniqueAssetPath()` which appends a counter suffix (e.g. `ProfileName1`, `ProfileName2`)

---

## UI Conventions

- All tab classes use a `Mode` enum (typically `List` and `CreateEdit`) to manage view state — no separate windows are opened
- `_isDirty` tracks unsaved changes in create/edit mode — triggers a discard confirmation dialog when the user tries to navigate back without saving
- `ReorderableList` is used for all list editing (folder paths, mapping rules, FBX rules, prefixes)
- `PristinePipelineWindow.DrawDivider()` is a shared static helper for drawing the horizontal separator line — use it between sections for consistency

---

## Local Setup

1. Clone this repository
2. Open a Unity project (2021.3 or later)
3. Go to **Window > Package Manager > + > Add package from disk...**
4. Point it to the `package.json` in this repo
5. The package will appear under **Packages** in your Project window

---

## Contributing

This is an open source project — feel free to contribute in any way you'd like! You can:

- Open an **issue** to report a bug or suggest a feature
- Submit a **pull request** with a fix or improvement
- Share feedback, ideas, or questions in the Discussions tab

There are no strict rules, but keeping pull requests focused (one fix or feature per PR) makes review much easier. All contributions are appreciated!
