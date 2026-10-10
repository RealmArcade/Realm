1. Create `Realm.Client/Services/TimerService.cs` and `Realm.Client/Services/RandomService.cs` to hold domain service states.
2. Verify creation of the service files via `cat`.
3. Modify `Realm.Client/Services/ServiceLocator.cs` to register `TimerService` and `RandomService`.
4. Verify `ServiceLocator.cs` modification via `git diff`.
5. Refactor `Realm.Client/Core/GameHost.cs` to replace `_staticTextLabels`, `_nextStaticTextHandle`, `_controlGroups`, `_lastGroupPressTime`, `_isDragging`, `_dragStart`, and `_dragEnd` with auto-properties, and replace `_nextTimerHandle`, `_scheduledTimers`, and `Rng` with forwarding property shims to the new services.
6. Verify `GameHost.cs` modification via `git diff`.
7. Run `dotnet build` to ensure the project compiles with 0 errors.
8. Run `export GODOT_BIN=/usr/bin/godot && export MSBUILDDISABLENODEREUSE=1 && dotnet test Realm.slnx` to ensure tests pass.
9. Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
10. Submit the changes.
