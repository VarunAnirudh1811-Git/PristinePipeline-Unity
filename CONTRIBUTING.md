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

* **Tab** — UI only. Owns the EditorWindow drawing logic and mode transitions. Never touches the filesystem or AssetDatabase directly.
* **Utility** — Stateless logic only. All file I/O, AssetDatabase calls, rule matching, material creation, prefab generation, and import configuration live here. Safe to call from any editor context, including processors.
* **Processor** — Unity's asset pipeline hook. Listens to import events via `AssetPostprocessor` and delegates all logic to the Utility class.

This means if you want to change how assets are moved, how FBX settings are applied, or how prefabs/materials are generated, you only ever touch the Utility class — not the Tab or the Processor.

---

### Workflow Philosophy (v1.1.0+)

The tool is designed around a **daily-use workflow**:

* Profiles and templates are configured once
* The primary interaction becomes repeatedly triggering actions (Generate / Organize / Reprocess)

UI decisions follow this principle:

* Primary actions must always be visible at the top
* Profile management is secondary and collapsible
* State should be readable at a glance (status pills, inline info)

When contributing to UI:

* Prioritize execution speed over configurability visibility
* Avoid pushing primary actions below scroll
* Keep interactions shallow — common actions should never require multiple steps

---

## Core Systems

### ToolSettings.cs

The single source of truth for all persisted settings. Wraps `EditorPrefs` with typed properties and namespaced keys.

* **No tool should ever call `EditorPrefs` directly** — always go through `ToolSettings`
* All keys are prefixed with `ToolInfo.SettingsPrefix` to avoid collisions with other Unity tools
* `ResetAll()` wipes every key at once — used by the Settings tab reset button
* Adding a new setting = add a private key constant, a public property with get/set, and a `DeleteKey` call in `ResetAll()`

```csharp
// Correct
bool enabled = ToolSettings.Organizer_Enabled;

// Wrong — never do this
bool enabled = EditorPrefs.GetBool("Organizer.Enabled");
```

---

### ProfileRegistry.cs

Sits between `ToolSettings` (which stores GUIDs) and the rest of the codebase (which needs actual asset references). Resolves GUIDs to ScriptableObject instances.

* **No tool should ever call `AssetDatabase.GUIDToAssetPath` for profile resolution** — always go through `ProfileRegistry`
* Uses a generic `Load<T>` / `Save<T>` pair internally — adding a new tool only requires adding a new pair of convenience methods at the bottom
* Returns `null` cleanly if the GUID is empty or the asset has been deleted or moved

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

* `isBuiltIn` is set only on package-bundled templates and is hidden from the Inspector
* When `isBuiltIn` is true, the Folder Generator tab disables Edit and Delete buttons for that template

---

### AssetMappingProfile

Stores a list of `MappingRule` objects. Each rule holds an extension, an optional wildcard name pattern, and a destination folder path.

* Extensions are stored **without** the leading dot (e.g. `png` not `.png`) for consistent comparison
* `HasNamePattern` is a convenience property — true when `namePattern` is not null or whitespace
* Rules are evaluated in order — **last match wins**

---

### FBXImportProfile

Stores a list of `FBXImportRule` objects, a default `FBXImportPreset`, naming convention rules, and additional automation settings.

* Each rule contains its own embedded `FBXImportPreset`
* Rules are evaluated in order — **last match wins**
* `defaultPreset` is applied with a warning when no rule matches

**Extended responsibilities (v1.1.0+):**

* Material creation and texture assignment
* Prefab generation (auto + manual)
* Material, texture, and prefab folder configuration
* Profile-level toggles (Emission / Ambient Occlusion)

Prefab generation must be deterministic — existing prefabs should never be overwritten.

---

## Rule Matching

Both the Asset Organizer and FBX Importer use the same wildcard matching strategy:

* Supports `*` (any sequence) and `?` (single character)
* Case-insensitive
* Implemented using dynamic programming (`MatchesWildcard()`)

**Last match wins** — rules are evaluated top to bottom, and the final match takes priority.

---

## FBX Import Pipeline Hook

`FBXImporterProcessor` extends `AssetPostprocessor` and overrides `OnPreprocessModel()`.

* This fires **before** Unity processes the model — correct place to override `ModelImporter`
* Processor exits early if:

  * File is not `.fbx`
  * Importer is disabled
  * No active profile exists

Naming enforcement blocks invalid files via exception.

---

### Extended Responsibilities (v1.1.0+)

The Processor should remain minimal:

1. Validate conditions
2. Fetch active profile
3. Delegate execution to Utility

The Utility layer handles:

* Import settings
* Material creation
* Texture assignment
* Prefab generation

Never move logic into the Processor.

---

## Profile Import / Export

All tools support JSON import/export using `JsonUtility`.

* Export uses `EditorUtility.SaveFilePanel`
* Import uses `EditorUtility.OpenFilePanel`
* Imported assets are always saved to configured paths
* Duplicate names handled via `BuildUniqueAssetPath()`

---

## UI Conventions

* Each tab uses a `Mode` enum (List / CreateEdit)
* `_isDirty` prevents accidental data loss
* `ReorderableList` is used for all editable lists

### Layout Rules (v1.1.0+)

* Primary action button at the top
* Profile/template selector directly below
* Inline asset ping button (**⊙**) instead of full row button
* Management UI inside a collapsible foldout at the bottom
* Rules preview uses scrollable container

Avoid introducing layouts that increase vertical friction.

---

## Adding a New Tool

1. Add new `TabID` in `PristinePipelineEditor.cs`
2. Add case in `DrawActiveTab()`
3. Create:

   * `MyToolTab.cs` (UI only)
   * `MyToolUtility.cs` (logic)
   * `MyToolProcessor.cs` (optional)
4. Add settings in `ToolSettings`
5. Add accessors in `ProfileRegistry`
6. Add data model in `Runtime/Data/` (if needed)

### UI Expectations

* Action-first layout
* Minimal interaction steps
* Consistent with existing tabs

---

## Local Setup

1. Clone repository
2. Open Unity (2021.3+)
3. Package Manager → Add from disk
4. Select `package.json`

---

## Contributing

You can:

* Open an **issue** (bug / feature)
* Submit a **pull request**
* Share ideas or feedback

Guidelines:

* Keep PRs focused (one feature/fix)
* Follow architecture separation strictly
* Do not introduce logic into UI or Processor layers
* Maintain consistency with existing naming and structure

All contributions are appreciated!

---

If you want, next step I’d recommend is:
👉 adding a **“Code Style / Naming Conventions”** section — that’s usually the next thing that starts drifting as contributors grow.
