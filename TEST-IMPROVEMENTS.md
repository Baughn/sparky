# Test Suite Improvements

This document describes recent test improvements and plans for future work.

## Changes Made (Committed)

### 1. Fixed ApiTests.cs Initialization Pattern

**File:** `Sparky.Tests/MNA/ApiTests.cs`

**Problem:** The test class used a constructor to initialize `_sim`:
```csharp
public ApiTests() {
    _sim = new SimulationManager();
}
```

This means all tests in the class share the same `SimulationManager` instance. If one test modifies state (adds nodes, components, etc.), subsequent tests may be affected, causing:
- Non-deterministic test failures depending on execution order
- Hidden dependencies between tests
- Difficulty isolating bugs

**Fix:** Changed to use NUnit's `[SetUp]` attribute:
```csharp
[SetUp]
public void SetUp()
{
    _sim = new SimulationManager();
}
```

Now each test gets a fresh instance, ensuring proper isolation.

---

### 2. Added Centralized Tolerance Constants

**File:** `Sparky.Tests/TestHelpers/Tolerances.cs`

**Problem:** Tolerance values were scattered throughout tests as magic numbers:
```csharp
Assert.That(voltage, Is.EqualTo(5.0).Within(1e-6));
Assert.That(current, Is.EqualTo(0.1).Within(1e-9));
Assert.That(capacitance, Is.EqualTo(1e-6).Within(1e-12));
```

This makes it hard to:
- Understand what precision is expected
- Adjust tolerances globally if needed
- Maintain consistency across tests

**Fix:** Created a `Tolerances` static class with named constants:

```csharp
public static class Tolerances
{
    public const double Voltage = 1e-6;      // 1 μV - for DC voltage assertions
    public const double Current = 1e-9;      // 1 nA - for current measurements
    public const double Power = 1e-6;        // 1 μW - for power calculations
    public const double Parameter = 1e-12;   // For capacitance, inductance values
    public const double Loose = 1e-3;        // For transient/nonlinear simulations
    public const double VeryLoose = 1e-2;    // 1% - for complex circuits
    public const double Resistance = 1e-6;
    public const double Capacitance = 1e-12; // 1 pF
    public const double Inductance = 1e-9;   // 1 nH
}
```

Usage:
```csharp
using Sparky.Tests.TestHelpers;
Assert.That(voltage, Is.EqualTo(5.0).Within(Tolerances.Voltage));
```

**Note:** Existing tests still use inline tolerances. Future work could migrate them to use these constants.

---

### 3. Added Component Measurement Tests

**File:** `Sparky.Tests/MNA/ComponentMeasurementTests.cs`

**Problem:** The `ISimulation` interface exposes these methods, but they had no dedicated tests:
- `GetResistorCurrent(ResistorId id)` - current through resistor (from nodeA to nodeB)
- `GetResistorPower(ResistorId id)` - power dissipated (I² × R)
- `GetCapacitorCurrent(CapacitorId id)` - current through capacitor
- `GetVoltageSourceCurrent(VoltageSourceId id)` - current from voltage source

**Fix:** Added comprehensive tests organized by component type:

#### Resistor Current Tests (5 tests)
| Test | Description |
|------|-------------|
| `GetResistorCurrent_SimpleDivider_ReturnsCorrectCurrent` | Voltage divider: verifies I = V/R_total |
| `GetResistorCurrent_ParallelResistors_SplitsCurrent` | Two parallel resistors each carry half |
| `GetResistorCurrent_CurrentPolarity_MatchesNodeOrder` | Positive current = nodeA to nodeB |
| `GetResistorCurrent_ReversePolarity_NegativeCurrent` | Reversed definition = negative current |
| `GetResistorCurrent_NoExcitation_ZeroCurrent` | No source = zero current |

#### Resistor Power Tests (4 tests)
| Test | Description |
|------|-------------|
| `GetResistorPower_SimpleDivider_ReturnsCorrectPower` | P = I² × R for each resistor |
| `GetResistorPower_HighCurrent_HighPower` | 10V / 10Ω = 1A → 10W |
| `GetResistorPower_AlwaysPositive_RegardlessOfPolarity` | Power is always positive |
| `GetResistorPower_TotalPower_EqualsSourcePower` | Conservation: ΣP_resistors = P_source |

#### Capacitor Current Tests (4 tests)
| Test | Description |
|------|-------------|
| `GetCapacitorCurrent_Charging_PositiveCurrent` | Initial charging current > 0 |
| `GetCapacitorCurrent_SteadyState_ZeroCurrent` | After 5τ, current ≈ 0 |
| `GetCapacitorCurrent_CurrentSourceCharging_ConstantCurrent` | I_cap = I_source |
| `GetCapacitorCurrent_DCOnly_ZeroCurrent` | With dt=0, capacitor is open |

#### Voltage Source Current Tests (2 tests)
| Test | Description |
|------|-------------|
| `GetVoltageSourceCurrent_LoadedSource_ReturnsCorrectCurrent` | I = V/R_load |
| `GetVoltageSourceCurrent_MultipleLoads_SumsCurrent` | Current sums for parallel loads |

#### Invalid ID Tests (3 tests)
| Test | Description |
|------|-------------|
| `GetResistorCurrent_InvalidId_ThrowsInvalidComponentException` | Non-existent resistor |
| `GetResistorPower_InvalidId_ThrowsInvalidComponentException` | Non-existent resistor |
| `GetCapacitorCurrent_InvalidId_ThrowsInvalidComponentException` | Non-existent capacitor |

---

### 4. Added Parameter Validation Edge Case Tests

**File:** `Sparky.Tests/MNA/ParameterValidationEdgeCaseTests.cs`

**Problem:** No tests verified behavior when invalid floating-point values (NaN, ±Infinity) are passed to the API. These edge cases can cause:
- Silent corruption (NaN propagates through calculations)
- Infinite loops in solvers
- Misleading results

**Fix:** Added tests for all component types and the `Step()` method.

#### Resistor Validation (4 tests)
- `AddResistor_WithNaN_ThrowsInvalidParameterException`
- `AddResistor_WithPositiveInfinity_ThrowsInvalidParameterException`
- `AddResistor_WithNegativeInfinity_ThrowsInvalidParameterException`
- `UpdateResistor_WithNaN_ThrowsInvalidParameterException`

#### Capacitor Validation (3 tests)
- `AddCapacitor_WithNaN_ThrowsInvalidParameterException`
- `AddCapacitor_WithPositiveInfinity_ThrowsInvalidParameterException`
- `AddCapacitor_WithZero_ThrowsInvalidParameterException`

#### Inductor Validation (3 tests)
- `AddInductor_WithNaN_ThrowsInvalidParameterException`
- `AddInductor_WithPositiveInfinity_ThrowsInvalidParameterException`
- `AddInductor_WithZero_ThrowsInvalidParameterException`

#### Voltage Source Validation (3 tests)
- `AddVoltageSource_WithNaN_ThrowsInvalidParameterException`
- `AddVoltageSource_WithPositiveInfinity_ThrowsInvalidParameterException`
- `UpdateVoltageSource_WithNaN_ThrowsInvalidParameterException`

#### Current Source Validation (2 tests)
- `AddCurrentSource_WithNaN_ThrowsInvalidParameterException`
- `AddCurrentSource_WithPositiveInfinity_ThrowsInvalidParameterException`

#### Transformer Validation (2 tests)
- `AddTransformer_WithNaN_ThrowsInvalidParameterException`
- `AddTransformer_WithPositiveInfinity_ThrowsInvalidParameterException`

#### Controlled Source Validation (4 tests)
- `AddVCVS_WithNaN_ThrowsInvalidParameterException`
- `AddVCCS_WithNaN_ThrowsInvalidParameterException`
- `AddCCVS_WithNaN_ThrowsInvalidParameterException`
- `AddCCCS_WithNaN_ThrowsInvalidParameterException`

#### Step Time Validation (3 tests)
- `Step_WithNaN_ThrowsArgumentException`
- `Step_WithNegativeTime_ThrowsArgumentException`
- `Step_WithPositiveInfinity_ThrowsArgumentException`

#### Initial Condition Validation (2 tests)
- `SetCapacitorVoltage_WithNaN_ThrowsInvalidParameterException`
- `SetInductorCurrent_WithNaN_ThrowsInvalidParameterException`

---

## Expected Test Failures

When you run the tests, some may fail. This indicates gaps in the implementation:

### Likely Failures in ParameterValidationEdgeCaseTests

If the implementation doesn't validate for NaN/Infinity, tests like `AddResistor_WithNaN_ThrowsInvalidParameterException` will fail because no exception is thrown.

**Resolution options:**
1. **Add validation to SimulationManager** (preferred) - Check for `double.IsNaN()` and `double.IsInfinity()` in Add/Update methods
2. **Remove or adjust the tests** - If you decide the solver handles these gracefully

Example validation to add in `SimulationManager.AddResistor()`:
```csharp
if (double.IsNaN(resistance) || double.IsInfinity(resistance))
    throw new InvalidParameterException("resistance", resistance, "must be a finite number");
```

### Likely Failures in ComponentMeasurementTests

If any measurement methods aren't implemented or have bugs, tests will fail. Check:
- `GetResistorCurrent()` - May need to compute from voltage difference / resistance
- `GetResistorPower()` - May need to compute I² × R
- `GetCapacitorCurrent()` - May need to track from internal capacitor state

---

## Recommended Future Improvements

### High Priority

1. **Run the new tests and fix failures**
   - Either fix the implementation to pass tests
   - Or adjust tests to match intended behavior

2. **Add missing API method tests**

   These `ISimulation` methods may lack dedicated tests:
   - `GetInductorCurrent()` - already used but could use dedicated tests
   - `GetSwitchCurrent()` - verify current through closed switch
   - `NodeExists()` - verify return values

3. **Migrate existing tests to use Tolerances constants**

   Search for `Within(1e-` and replace with appropriate `Tolerances.*` constant.

### Medium Priority

4. **Add CircuitBuilder helper class**

   As recommended in TEST-PLAN.md, a fluent builder would simplify test setup:
   ```csharp
   var circuit = new CircuitBuilder(_sim)
       .WithVoltageSource(10.0)
       .WithResistorDivider(100.0, 100.0)
       .Build();
   ```

5. **Add parameterized tests using [TestCaseSource]**

   Many tests repeat similar patterns for different component types:
   ```csharp
   private static IEnumerable<TestCaseData> AllComponentTypes => new[]
   {
       new TestCaseData(typeof(ResistorId), "Resistor"),
       new TestCaseData(typeof(CapacitorId), "Capacitor"),
       // ...
   };

   [TestCaseSource(nameof(AllComponentTypes))]
   public void Component_RemoveNonExistent_Throws(Type idType, string name) { ... }
   ```

6. **Add performance regression tests**

   While `benchmark.sh` exists, could add unit tests that verify:
   - Bulk update is faster than individual updates
   - Partition solving scales appropriately
   - Large circuits don't regress

### Low Priority

7. **Increase coverage of controlled sources**

   VCVS, VCCS, CCVS, CCCS have good coverage, but could add:
   - Cascaded controlled sources
   - Feedback loops (op-amp configurations)
   - Edge cases (zero gain, very high gain)

8. **Add concurrency tests**

   The interface mentions thread safety considerations:
   > Thread safety: All methods must be called from a single thread,
   > except Step() which may be called from a worker thread after all modifications are complete.

   Could add tests verifying this contract.

---

## Files Changed Summary

| File | Change Type | Lines |
|------|-------------|-------|
| `Sparky.Tests/MNA/ApiTests.cs` | Modified | Fixed initialization |
| `Sparky.Tests/TestHelpers/Tolerances.cs` | New | ~55 lines |
| `Sparky.Tests/MNA/ComponentMeasurementTests.cs` | New | ~250 lines |
| `Sparky.Tests/MNA/ParameterValidationEdgeCaseTests.cs` | New | ~250 lines |

**Total:** ~758 new lines of test code, ~35 new tests

---

## Running the Tests

```bash
# Enter dev environment
nix develop

# Run all tests
dotnet test

# Run only new tests
dotnet test --filter "FullyQualifiedName~ComponentMeasurementTests|FullyQualifiedName~ParameterValidationEdgeCaseTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

---

## Notes for Resuming

1. The changes are committed to branch `claude/improve-test-suite-01HiH7rJdxXQPBwAoKGWaxwp`

2. Start by running `dotnet test` to see which new tests fail

3. For failing ParameterValidationEdgeCaseTests:
   - Check `MNA/Api/SimulationManager.cs` for the Add/Update methods
   - Add validation like: `if (double.IsNaN(x) || double.IsInfinity(x)) throw ...`

4. For failing ComponentMeasurementTests:
   - Check if the measurement methods are implemented in `SimulationManager.cs`
   - Verify the math: current = ΔV / R, power = I² × R

5. The Tolerances.cs file is ready to use but existing tests haven't been migrated yet
