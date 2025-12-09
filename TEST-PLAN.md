# Test Plan for Sparky Circuit Simulator

This document outlines test coverage gaps and recommended test additions for the Sparky MNA circuit solver.

## Current State

- **106 tests** across 14 test files
- **~2,800 lines** of test code covering **~3,000 lines** of production code
- Core solver and API layer have good coverage; Phase 1 & 2 complete

---

## Phase 1: Critical Coverage

### 1.1 API Exception Handling ✅ COMPLETED

**File:** `Sparky.Tests/MNA/ApiExceptionTests.cs`

13 tests implemented covering all exception types with property assertions.

---

### 1.2 API Component Lifecycle ✅ COMPLETED

**File:** `Sparky.Tests/MNA/ApiComponentLifecycleTests.cs`

17 tests implemented covering full CRUD lifecycle for all component types through the API.

| Test Name | Description |
|-----------|-------------|
| `CurrentSource_AddUpdateRemove_WorksCorrectly` | Full lifecycle |
| `CurrentSource_GetValue_ReturnsCorrectCurrent` | Readback after add |
| `Capacitor_AddUpdateRemove_WorksCorrectly` | Full lifecycle |
| `Capacitor_GetCapacitance_ReturnsCorrectValue` | Readback after add |
| `Capacitor_GetVoltage_ReturnsVoltageDifference` | Voltage across capacitor |
| `Inductor_AddUpdateRemove_WorksCorrectly` | Full lifecycle |
| `Inductor_GetInductance_ReturnsCorrectValue` | Readback after add |
| `Diode_AddRemove_WorksCorrectly` | Full lifecycle (no update) |
| `Diode_GetVoltage_ReturnsAnodeCathodeDifference` | Voltage readback |
| `Transformer_AddUpdateRemove_WorksCorrectly` | Full lifecycle |
| `Transformer_GetRatio_ReturnsCorrectValue` | Readback after add |
| `Transformer_GetCurrents_ReturnsPrimaryAndSecondary` | Current readback |
| `Node_CreateAndRemove_WorksCorrectly` | Node lifecycle |
| `Node_RemoveAfterComponentRemoval_Succeeds` | Remove component first, then node |
| `Clear_RemovesAllComponentsAndNodes` | Reset simulation |
| `ComponentExists_ReturnsTrueForExisting` | Existence checks for all types |
| `ComponentExists_ReturnsFalseAfterRemoval` | Existence after removal |

**Maximizing usefulness:**
- Create a base test class with helper methods for common circuit setups
- Test that removal properly cleans up (no dangling references affecting subsequent operations)
- Verify component IDs are not reused after removal (or document if they are)

---

### 1.3 Bulk Update Mechanism ✅ COMPLETED

**File:** `Sparky.Tests/MNA/BulkUpdateTests.cs`

6 tests implemented covering deferred rebuilds, nested scopes, and dispose safety.

| Test Name | Description |
|-----------|-------------|
| `BulkUpdate_DefersRebuild_UntilDispose` | Verify no rebuild during scope |
| `BulkUpdate_MultipleChanges_SingleRebuild` | Efficiency check |
| `BulkUpdate_Nested_OnlyRebuildsOnOuterDispose` | Nested scopes work correctly |
| `BulkUpdate_DisposedTwice_NoError` | Idempotent dispose |
| `BulkUpdate_WithException_StillDisposes` | Using statement safety |
| `BulkUpdate_StepAfterDispose_Works` | Normal operation resumes |

**Maximizing usefulness:**
- Add a way to detect rebuild count (or use partition count changes as proxy)
- Test performance: bulk update with 100 changes should be faster than 100 individual changes

---

## Phase 2: Important Coverage

### 2.1 Line Optimization Edge Cases ✅ COMPLETED

**File:** `Sparky.Tests/MNA/LineOptimizationTests.cs`

9 tests implemented covering chain merging, partial merges, optimization toggling, and voltage interpolation.

---

### 2.2 Solver Path Selection ✅ COMPLETED

**File:** `Sparky.Tests/SolverPathTests.cs`

6 tests implemented verifying dense/sparse solver selection thresholds and result equivalence.

Added `Circuit.LastUsedDenseSolver` diagnostic property and `InternalsVisibleTo` for test access.

---

### 2.3 Current Source Tests ✅ COMPLETED

**File:** `Sparky.Tests/CurrentSourceTests.cs`

6 tests implemented covering polarity, superposition, capacitor charging, and updates.

---

### 2.4 Diagnostics and Stats ✅ COMPLETED

**File:** `Sparky.Tests/DiagnosticsTests.cs`

8 tests implemented covering GetStats, PartitionCount, and iteration tracking.

---

## Phase 3: Completeness

### 3.1 Edge Case Circuits ✅ COMPLETED

**File:** `Sparky.Tests/EdgeCaseCircuitTests.cs`

8 tests implemented covering empty circuits, orphan nodes, degenerate configurations, and numerical stability.

| Test Name | Description |
|-----------|-------------|
| `EmptyCircuit_Step_DoesNotThrow` | No components |
| `SingleNode_NoComponents_StepSucceeds` | Orphan node at ground potential |
| `ResistorToGround_NoSource_ZeroVoltage` | No excitation |
| `ParallelVoltageSources_SameVoltage_SingularMatrix` | Documents singular matrix behavior |
| `ParallelVoltageSources_DifferentVoltage_Behavior` | Documents conflicting sources behavior |
| `VeryLargeCircuit_1000Nodes_Succeeds` | Stress test with 1000-node ladder |
| `VerySmallValues_PicoFarads_Succeeds` | Numerical stability with 1pF capacitor |
| `VeryLargeValues_Megohms_Succeeds` | Numerical stability with 1MΩ resistors |

**Key finding:** Parallel ideal voltage sources always create singular matrices (even with identical voltages) because the current distribution is indeterminate in MNA.

---

### 3.2 Advanced Integration Scenarios

**File:** `Sparky.Tests/AdvancedScenarioTests.cs`

| Test Name | Description |
|-----------|-------------|
| `FullWaveBridgeRectifier_ProducesDC` | Four diodes, smoother output |
| `LCOscillator_Oscillates` | Energy transfers between L and C |
| `RLCResonance_PeaksAtNaturalFrequency` | f = 1/(2*pi*sqrt(LC)) |
| `CascadedTransformers_MultiplyRatios` | Two transformers in series |
| `VoltageRegulator_ClampsOutput` | Zener-like behavior with diode |
| `MotorWithBackEMF_CurrentLimits` | Inductor + voltage source |

**Maximizing usefulness:**
- These scenarios match real-world use cases in the game
- Use longer simulation times to verify steady-state behavior

---

### 3.3 Parallel Partition Execution

**File:** `Sparky.Tests/ParallelPartitionTests.cs`

| Test Name | Description |
|-----------|-------------|
| `TwoPartitions_BothSolveCorrectly` | Independence verified |
| `ManyPartitions_AllSolveCorrectly` | Scale test |
| `PartitionsWithDifferentComplexity_AllComplete` | Mixed linear/nonlinear |
| `ConnectingPartitions_MergesIntoOne` | Topology change |

**Maximizing usefulness:**
- Run these tests multiple times to catch race conditions
- Verify results are deterministic despite parallel execution

---

## Test Infrastructure Recommendations

### Shared Test Utilities

Create `Sparky.Tests/TestHelpers/CircuitBuilder.cs`:
```csharp
// Fluent API for common circuit patterns
var circuit = new CircuitBuilder()
    .WithVoltageSource(10.0)
    .WithResistorDivider(100.0, 100.0)
    .Build();
```

### Tolerance Constants

Create `Sparky.Tests/TestHelpers/Tolerances.cs`:
```csharp
public static class Tolerances
{
    public const double Voltage = 1e-6;
    public const double Current = 1e-9;
    public const double Power = 1e-6;
    public const double Loose = 1e-3;  // For transient/nonlinear
}
```

### Parameterized Component Tests

Use NUnit's `[TestCaseSource]` to test all component types with shared logic:
```csharp
[TestCaseSource(nameof(AllComponentTypes))]
public void Component_RemoveNonExistent_Throws(ComponentType type) { ... }
```

---

## Priority Summary

| Priority | Category | Tests | Effort |
|----------|----------|-------|--------|
| P0 | API Exceptions | ✅ 13 done | Low |
| P0 | API Component Lifecycle | ✅ 17 done | Medium |
| P0 | Bulk Update | ✅ 6 done | Low |
| P1 | Line Optimization | ✅ 9 done | Medium |
| P1 | Solver Path | ✅ 6 done | Low |
| P1 | Current Source | ✅ 6 done | Low |
| P1 | Diagnostics | ✅ 8 done | Low |
| P2 | Edge Cases | ✅ 8 done | Medium |
| P2 | Advanced Scenarios | 6 | High |
| P2 | Parallel Execution | 4 | Medium |

**Total: ~82 new tests** (73 completed, 10 remaining in Phase 3)

---

## Success Metrics

After implementing this plan:
- All public API methods should have at least one test
- All exception types should be tested
- Code coverage should increase from ~60% to ~85%+
- No untested public methods in `ISimulation` interface
