\# Contributing to Pristine Pipeline



Pristine Pipeline is an open source project and contributions are welcome! Whether you want to fix a bug, suggest a feature, or explore the codebase — this document is for you.



\---



\## 🗂️ Project Structure



```

Packages/com.glyphlabs.pristinepipeline/

&#x20; Editor/

&#x20;   Core/                  ← Main window, settings, and profile registry

&#x20;   FolderGenerator/       ← Folder Generator tab and utilities

&#x20;   AssetOrganizer/        ← Asset Organizer tab, processor, and utilities

&#x20;   FBXImporter/           ← FBX Importer tab, processor, and utilities

&#x20; Runtime/

&#x20;   Data/                  ← ScriptableObject data types (templates and profiles)

&#x20; package.json             ← UPM package manifest

```



\*\*Runtime vs Editor:\*\* Data assets (`FolderTemplate`, `AssetMappingProfile`, `FBXImportProfile`) live in `Runtime/` so they are accessible as ScriptableObjects across the project. All tool logic lives in `Editor/` and is stripped from builds.



\---



\## Architecture Notes



\- \*\*No tool should call `EditorPrefs` directly\*\* — all settings go through `ToolSettings.cs`

\- \*\*No tool should resolve profile GUIDs directly\*\* — all profile lookups go through `ProfileRegistry.cs`

\- \*\*All asset operations\*\* (file I/O, AssetDatabase calls) live in the `\*Utility.cs` classes — the tab classes are UI only

\- \*\*Adding a new tool\*\* = add a tab ID and label in `PristinePipelineEditor.cs`, create a `Tab` class and a `Utility` class, and add a settings entry in `ToolSettings.cs`. Nothing else needs to change.



\---



\## Local Setup



1\. Clone this repository

2\. Open a Unity project (2021.3 or later)

3\. Go to \*\*Window → Package Manager → + → Add package from disk...\*\*

4\. Point it to the `package.json` in this repo

5\. The package will appear under \*\*Packages\*\* in your Project window



\---



\## Contributing



This is an open source project — feel free to contribute in any way you'd like! You can:



\- Open an \*\*issue\*\* to report a bug or suggest a feature

\- Submit a \*\*pull request\*\* with a fix or improvement

\- Share feedback, ideas, or questions in the Discussions tab



There are no strict rules, but keeping pull requests focused (one fix or feature per PR) makes review much easier. All contributions are appreciated!

