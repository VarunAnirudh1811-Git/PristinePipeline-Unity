# Changelog

All notable changes to Pristine Pipeline are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.2.1] — 2026-04-13

### Fixed
- Asset Organizer no longer processes the entire project, preventing unintended changes to plugins and external assets

### Added
- Scope control system:
  - Introduced a unified **scope model** for Asset Organizer operations:
	- **Assets/** (top-level only, non-recursive)
	- **Active Root** (recursive)
	- **User-defined additional scope folders** (recursive)
  - Added UI to manage additional folders

### Changed
- Asset Organizer now operates within a defined scope instead of globally

---

## [1.2.0] — 2026-04-13

### Added — Active Root System (Core Architecture)

- Introduced a global **Active Root** system that defines the working context for all tools
- Active Root defaults to `Assets` and is always validated (never null or invalid)
- All folder paths in templates, mapping profiles, and FBX import presets are now **relative to Active Root**
- Added centralized path resolution via `ToolSettings.ResolveRelativeToActiveRoot()` to ensure consistent path construction across all systems


### Added — Global Context Bar (UI)

- Added an **Active Root bar** at the top of the main window
- Displays current working root for all operations
- Includes quick actions:
  - **Ping** — highlights the root in Project window
  - **Change** — select a different root folder
  - **New Folder** — create a new root directly from the tool
  - **Reset** — revert to default `Assets`
- Missing or invalid roots are now detected and shown with a warning state

### Added — Inline Root Creation Workflow

- Users can now create a new root folder directly inside the tool
- Inline creation mode with validation and error feedback
- Automatically sets the newly created folder as Active Root

### Changed — Unified Path Handling

- Removed reliance on hardcoded `"Assets/"` paths across the codebase
- All tools (Folder Generator, Asset Organizer, FBX Importer) now resolve paths through Active Root
- Profiles are now:
  - Portable
  - Root-independent
  - Consistent across projects

### Changed — Responsive UI Layout

- Action buttons in Active Root bar now use **dynamic width distribution**
- Buttons scale proportionally with window size for consistent layout
- Improved visual alignment and reduced layout inconsistencies

### Improved — System Cohesion

- Folder Generator now acts as a **context initializer** instead of passing root paths manually
- Removed redundant root handling across utilities
- All tools now operate within a unified execution context

---

## [1.1.1] — 2026-04-03

### Added — Built-in Profile Detection

- Profiles located in `Packages/` are now automatically marked as built-in
- Built-in profiles are shown with a **"(Built-in)"** suffix in dropdowns

### Changed

- Built-in status is now determined at load time instead of being stored or imported

### Removed — Compatibility

- Removed support for older Unity versions that use the non-generic TreeView API
- Pristine Pipeline now requires Unity 6000.3 or newer due to TreeView API changes

### Improved

- Minor code cleanup and formatting improvements


___



## [1.1.0] — 2026-04-03

### Added — FBX Importer

- **Material & texture automation**
  - Automatically creates materials and assigns textures based on naming conventions
  - Supports configurable material and texture folder paths per preset

- **Prefab generation system**
  - Option to automatically generate prefabs on import
  - Prefabs can be marked as lightmap static
  - Added **Generate Prefabs Now** button to create prefabs for all FBX files manually
  - Existing prefabs are never overwritten

- **Profile-level texture toggles**
  - **Enable Emission** — assigns `_E` / `_Emissive` textures to emission slot
  - **Enable Ambient Occlusion** — assigns `_AO` textures unless ORM map is present

---

### Changed — Editor UX (all three tabs)

**Action-first layout.**  
The primary action button in every tab has been promoted to the top of the tab so it is visible without scrolling. The tool is now optimized for a daily workflow where profiles are configured once and actions are triggered repeatedly.

- Folder Generator — **▶ Generate Folder Structure** moved to top with active template inline
- Asset Organizer — **▶ Organize Project Now** moved to top with active profile inline
- FBX Importer — **▶ Reprocess All FBX Files** (primary) and **Generate Prefabs Now** (secondary) placed at top with distinct visual hierarchy

**Collapsible profile / template management.**  
All profile/template editing actions (New, Edit, Duplicate, Delete, Import, Export) are now grouped inside a collapsible **Manage Profiles / Templates** foldout at the bottom of each tab. Collapsed by default.

**Compact profile selector.**  
Dropdown simplified into a single compact row with inline asset ping button (**⊙**).

**Coloured status pill.**  
Enabled/disabled states now shown as:
- Green **● Active**
- Grey **● Off**

**Scrollable rules preview.**  
Rules tables now use a scroll view with a fixed max height to prevent UI overflow on large rule sets.

---

### Improved — Workflow Design

- Tool now follows a **“configure once, use daily”** workflow model
- Primary actions are always visible and require minimal interaction
- Profile management is treated as secondary UI

---

## [1.0.1] — Bug Fixes

### Fixed

- Default profiles not appearing in Asset Organizer and FBX Importer profile lists
- Asset skip counter logic in Asset Organizer
- FBX import blocking using `LogError + return` instead of throwing an exception
- Folder creation using incorrect relative paths instead of absolute filesystem paths
- Destination folder being created before collision check in Asset Organizer
- ScriptableObject cloning using `Object.Instantiate` instead of `CreateInstance`
- UnityEditor types leaking into Runtime assembly behind `#if UNITY_EDITOR` guards
- TreeView item IDs using non-deterministic `GetHashCode`
- FBX reprocess loop missing `StartAssetEditing` / `StopAssetEditing` wrapper
- Duplicate profile detection using `AssetDatabase` instead of `File.Exists`
- Folder path sanitization mutating backing list as a side-effect of validation

---

## [1.0.0] — Initial Release

### Added

- **Folder Generator**
  - Create folder structures from reusable templates
  - Preview before generating
  - Export/import templates as JSON
  - Optional `.keep` files for Git tracking

- **Asset Organizer**
  - Rule-based automatic asset sorting on import
  - Wildcard name pattern support
  - Manual organize pass
  - Export/import profiles as JSON

- **FBX Importer**
  - Rule-based import preset application
  - Naming convention enforcement (enforce or warn)
  - Reprocess all FBX files in project
  - Export/import profiles as JSON

- Two built-in templates (Minimal, Standard) and matching profiles for each tool

- Settings tab for configuring save paths and resetting preferences
