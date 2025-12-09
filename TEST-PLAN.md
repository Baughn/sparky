# Test Plan for Sparky Circuit Simulator

This document outlines test coverage gaps and recommended test additions for the Sparky MNA circuit solver.

## Current State

- **38 tests** across 10 test files
- **~1,400 lines** of test code covering **~3,000 lines** of production code
- Core solver has reasonable coverage; API layer is undertested

---

## Phase 1: Critical Coverage

### 1.1 API Exception Handling

**File:** `Sparky.Tests/MNA/ApiExceptionTests.cs`

These tests verify the API properly rejects invalid operations with appropriate exceptions.

| Test Name | Description |
|-----------|-------------|
| `AddResistor_WithInvalidNode_ThrowsInvalidNodeException` | Reference non-existent node |
| `AddResistor_WithNegativeResistance_ThrowsInvalidParameterException` | Reject R ≤ 0 |
| `AddResistor_WithZeroResistance_ThrowsInvalidParameterException` | Reject R = 0 |
| `AddCapacitor_WithNegativeCapacitance_ThrowsInvalidParameterException` | Reject C ≤ 0 |
| `AddInductor_WithNegativeInductance_ThrowsInvalidParameterException` | Reject L ≤ 0 |
| `AddTransformer_WithZeroRatio_ThrowsInvalidParameterException` | Reject ratio ≤ 0 |
| `UpdateResistor_WithInvalidId_ThrowsInvalidComponentException` | Non-existent resistor |
| `RemoveResistor_WithInvalidId_ThrowsInvalidComponentException` | Non-existent resistor |
| `RemoveNode_WithConnections_ThrowsNodeInUseException` | Node still has components |
| `RemoveNode_Ground_ThrowsInvalidOperationException` | Cannot remove ground |
| `Step_DuringBulkUpdate_ThrowsInvalidOperationException` | Step not allowed in bulk update |
| `GetVoltage_InvalidNode_ThrowsInvalidNodeException` | Query non-existent node |

**Maximizing usefulness:**
- Use `[TestCase]` attributes to test each component type with the same invalid parameter patterns
- Verify exception messages contain useful debugging information (node ID, component type, etc.)

---

### 1.2 API Component Lifecycle

**File:** `Sparky.Tests/MNA/ApiComponentLifecycleTests.cs`

Full CRUD coverage for all component types through the API.

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

### 1.3 Bulk Update Mechanism

**File:** `Sparky.Tests/MNA/BulkUpdateTests.cs`

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

### 2.1 Line Optimization Edge Cases

**File:** `Sparky.Tests/MNA/LineOptimizationTests.cs`

| Test Name | Description |
|-----------|-------------|
| `Optimization_ChainBrokenByCapacitor_PartialMerge` | Non-resistor interrupts chain |
| `Optimization_ChainBrokenByVoltageSource_PartialMerge` | Source interrupts chain |
| `Optimization_SingleResistor_NoMerge` | Nothing to optimize |
| `Optimization_BranchingNetwork_OnlyMergesLines` | T-junction not merged |
| `Optimization_Disabled_NoInterpolation` | All nodes physical |
| `Optimization_EnabledMidSimulation_Rebuilds` | Toggle triggers rebuild |
| `Optimization_InterpolatedVoltage_MatchesExpected` | Math correctness |
| `Optimization_ThreeResistorChain_CorrectInterpolation` | Middle node interpolated |
| `Optimization_IsNodeOptimized_ReturnsTrue` | Diagnostic method works |

**Maximizing usefulness:**
- Compare optimized vs non-optimized results for same circuit (should match within tolerance)
- Measure node count reduction to verify optimization is occurring

---

### 2.2 Solver Path Selection

**File:** `Sparky.Tests/SolverPathTests.cs`

| Test Name | Description |
|-----------|-------------|
| `SmallCircuit_UsesDenseSolver` | < 96 nodes uses dense |
| `LargeCircuit_UsesSparseSOlver` | > 96 nodes uses sparse |
| `AtThreshold_UsesDenseSolver` | Exactly 96 nodes |
| `JustAboveThreshold_UsesSparse` | 97 nodes |
| `DenseMatrix_AboveThreshold_StillUsesDense` | High density triggers dense |
| `BothPaths_ProduceSameResult` | Correctness check |

**Maximizing usefulness:**
- These tests verify the threshold logic at `Circuit.cs:410-417`
- Add a diagnostic to expose which solver path was used, or infer from timing

---

### 2.3 Current Source Tests

**File:** `Sparky.Tests/CurrentSourceTests.cs`

| Test Name | Description |
|-----------|-------------|
| `CurrentSource_SetsNodeVoltage` | Basic operation with resistor |
| `CurrentSource_Polarity_PositiveCurrentFlowsInToOut` | Direction convention |
| `CurrentSource_MultipleInParallel_CurrentsAdd` | Superposition |
| `CurrentSource_WithCapacitor_ChargesLinearly` | I = C * dV/dt |
| `CurrentSource_ZeroCurrent_NoEffect` | Degenerate case |
| `CurrentSource_Update_AffectsNextStep` | Mutation works |

**Maximizing usefulness:**
- Current sources are fundamental for modeling real-world sources; thorough testing prevents subtle bugs

---

### 2.4 Diagnostics and Stats

**File:** `Sparky.Tests/DiagnosticsTests.cs`

| Test Name | Description |
|-----------|-------------|
| `GetStats_ReturnsCorrectPartitionCount` | Matches PartitionCount property |
| `GetStats_ReturnsCorrectPhysicalNodeCount` | Non-optimized nodes |
| `GetStats_ReturnsCorrectOptimizedNodeCount` | Interpolated nodes |
| `GetStats_TotalIterations_SumsPartitions` | Iteration tracking |
| `PartitionCount_TwoDisconnectedCircuits_ReturnsTwo` | Partitioning works |
| `PartitionCount_AfterClear_ReturnsZero` | Reset state |
| `LastIterations_LinearCircuit_ReturnsOne` | No Newton iterations needed |
| `LastIterations_WithDiode_ReturnsMultiple` | Newton iterations occur |

**Maximizing usefulness:**
- These tests document expected diagnostic behavior for users monitoring simulation health

---

## Phase 3: Completeness

### 3.1 Edge Case Circuits

**File:** `Sparky.Tests/EdgeCaseCircuitTests.cs`

| Test Name | Description |
|-----------|-------------|
| `EmptyCircuit_Step_DoesNotThrow` | No components |
| `SingleNode_NoComponents_StepSucceeds` | Orphan node |
| `ResistorToGround_NoSource_ZeroVoltage` | No excitation |
| `ParallelVoltageSources_SameVoltage_Works` | Valid configuration |
| `ParallelVoltageSources_DifferentVoltage_Behavior` | Document behavior |
| `VeryLargeCircuit_1000Nodes_Succeeds` | Stress test |
| `VerySmallValues_PicoFarads_Succeeds` | Numerical stability |
| `VeryLargeValues_Megohms_Succeeds` | Numerical stability |

**Maximizing usefulness:**
- Edge cases often reveal numerical instability or assumptions in the solver
- Document expected behavior for degenerate configurations

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
| P0 | API Exceptions | 12 | Low |
| P0 | API Component Lifecycle | 17 | Medium |
| P0 | Bulk Update | 6 | Low |
| P1 | Line Optimization | 9 | Medium |
| P1 | Solver Path | 6 | Low |
| P1 | Current Source | 6 | Low |
| P1 | Diagnostics | 8 | Low |
| P2 | Edge Cases | 8 | Medium |
| P2 | Advanced Scenarios | 6 | High |
| P2 | Parallel Execution | 4 | Medium |

**Total: ~82 new tests**

---

## Success Metrics

After implementing this plan:
- All public API methods should have at least one test
- All exception types should be tested
- Code coverage should increase from ~60% to ~85%+
- No untested public methods in `ISimulation` interface
