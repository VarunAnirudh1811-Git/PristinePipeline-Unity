# Contributing to Pristine Pipeline

Pristine Pipeline is an open source Unity Editor tool. Contributions are welcome! Whether you want to fix a bug, suggest a feature, or explore the codebase — this document is for you.

This document defines architecture, rules, and constraints for safe contributions.

---

## 🗂️ Project Structure

```
Packages/com.glyphlabs.pristinepipeline/
  Editor/
    Core/                  ← Main window, settings, and profile registry
    FolderGenerator/       ← Folder Generator tab and utilities
    AssetOrganizer/        ← Asset Organizer tab, processor, and utilities
    FBXImporter/           ← FBX Importer tab, processor, and utilities
  Resources/
    FolderTemplates/       ← Folder templates
    AssetMappingProfiles/  ← Asset mapping profiles
    ImporterProfiles/      ← FBX import profiles
  Runtime/
     Data/                 ← ScriptableObject data types (templates and profiles)
```

---

## Architecture Overview

Pristine Pipeline follows a strict separation of concerns. Every tool is split into three layers:

* **Tab** — UI only. Owns the EditorWindow drawing logic and mode transitions. Never touches the filesystem or AssetDatabase directly.
* **Utility** — Stateless logic only. All file I/O, AssetDatabase calls, rule matching, material creation, prefab generation, and import configuration live here. Safe to call from any editor context, including processors.
* **Processor** — Unity's asset pipeline hook. Listens to import events via `AssetPostprocessor` and delegates all logic to the Utility class.

---

## 🌐 Execution Context — Active Root (v1.2.0)

All tools operate inside:

```csharp
ToolSettings.ActiveRootPath
```

This is the single source of truth for all path operations.

---

## Rules

* Never assume `"Assets/"` as root
* Never hardcode paths
* Never pass root paths manually
* All tools must respect Active Root

---

## Path Resolution

```csharp
ToolSettings.Resolve(relativePath);
```

---

## Path Format

| Correct      | Incorrect           |
| ------------ | ------------------- |
| Art/Textures | Assets/Art/Textures |

---

## Scope Enforcement (v1.2.1+)

Asset Organizer must only process assets within defined scope.

Scope includes:

1. Assets/ (top-level only, non-recursive)
2. Active Root (recursive)
3. Additional scope paths (recursive)

---

All processing must use:

AssetOrganizerUtility.IsInScope(assetPath)
---

## ⚠️ Safety Requirements

### Asset Organizer

- Must respect scope rules
- Must never operate on entire project
- Must ignore assets outside defined scope

---

## Design Principles

### 1. Single Source of Truth

Active Root defines execution context.

### 2. Context-Aware Systems

All tools must behave consistently within Active Root.

### 3. Predictability

No hidden or implicit behavior.

### 4. Safety First

Prefer skipping over destructive operations.

---

## Core Systems

### ToolSettings

* Centralized EditorPrefs wrapper
* No direct EditorPrefs usage elsewhere

### ProfileRegistry

* Resolves GUIDs → ScriptableObjects
* Avoid direct AssetDatabase lookups

---

## Data Models

* FolderTemplate
* AssetMappingProfile
* FBXImportProfile

All paths must be relative to Active Root.

---

## Asset Organizer Notes

* Always filter using Active Root
* Never process full project blindly
* Prefer skipping when uncertain
* Never process assets without calling IsInScope first

---

## UI Guidelines

* Action-first layout
* Primary buttons always visible
* Minimal interaction depth

---

## Adding a New Tool

1. Add Tab
2. Create Tab / Utility / Processor
3. Add settings
4. Ensure Active Root compliance

---

## Common Mistakes

### ❌ Hardcoding paths

```csharp
"Assets/" + folder
```

### ✅ Correct

```csharp
ToolSettings.Resolve(folder);
```

---

### ❌ Processing all assets

```csharp
AssetDatabase.GetAllAssetPaths()
```

### ✅ Correct

Filter using Active Root

---

### ❌ Skipping scope validation

Applying rules directly to assets without checking scope

### ✅ Correct

Always validate using:

AssetOrganizerUtility.IsInScope(assetPath)

---

## Testing

### Test with:

  - Only Active Root
  - Active Root + additional paths

### Verify:

- Plugins are untouched
- External folders are ignored
- Only allowed assets are processed

---

## Contributing

* Keep PRs focused
* Follow architecture rules
* Maintain consistency

---

## Final Note

Pristine Pipeline is a **context-driven pipeline system**.

All contributions must respect:

> Active Root as execution context
> Safety as a core constraint
