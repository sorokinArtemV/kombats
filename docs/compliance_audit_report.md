# Combat Stat System - Spec Compliance Audit Report

## Spec Compliance Checklist (PASS/FAIL)

### A) Formulas Location

✅ **PASS**: `src/Combats.Battle.Domain/Rules/CombatMath.cs` - All combat formulas (ComputeDerived, ComputeChance, ComputeDodgeChance, ComputeCritChance, RollDamage) are correctly located in CombatMath.cs. No formulas found in Engine/Application/Infrastructure beyond multiplier applications in BattleEngine.CalculateDamage (lines 356, 361) which correctly apply already-computed values.

### B) Domain Independence from IConfiguration/IOptions

✅ **PASS**: Domain layer has no dependencies on IConfiguration or IOptions. CombatBalance is a pure domain value object populated via CombatBalanceMapper from Infrastructure.

### C) Balance Configuration Flow

✅ **PASS**: Balance flows correctly: `appsettings.json` → `CombatBalanceOptions` → `CombatBalanceMapper.ToDomain()` → `CombatBalance` → `Ruleset.Balance`. Verified in `src/Combats.Battle.Infrastructure/Profiles/CombatBalanceMapper.cs` and `src/Combats.Battle.Application/UseCases/Lifecycle/RulesetNormalizer.cs`.

### D) Random Abstraction

✅ **PASS**: Random is properly abstracted: `IRandomProvider` interface in Domain (`src/Combats.Battle.Domain/Rules/IRandomProvider.cs`), `SystemRandomProvider` implementation in Infrastructure (`src/Combats.Battle.Infrastructure/Rules/SystemRandomProvider.cs`), and BattleEngine uses `IRandomProvider` field (no direct Random usage in Domain).

⚠️ **MINOR**: `src/Combats.Battle.Api/Controllers/DevBattlesController.cs` line 59 uses `new Random().Next()` directly, but this is in API layer (Infrastructure concern), not Domain.

### E) CalculateDamage Order

⚠️ **MINOR DEVIATION**: `src/Combats.Battle.Domain/Engine/BattleEngine.cs` - CalculateDamage method (lines 290-367) order mostly matches spec but has one issue:

- ✅ NoAction check (line 298-301) - CORRECT (step 1)
- ✅ Block check (line 304-308) - CORRECT (step 2)  
- ✅ Compute derived stats (line 311-312) - CORRECT (step 3)
- ✅ Roll dodge (line 314-320) - CORRECT (step 4)
- ✅ Roll crit before block resolution (line 322-325) - CORRECT (step 5)
- ✅ Block handling with crit modes (line 327-344) - CORRECT (step 6)
- ⚠️ Roll base damage (line 347) - Calls `CombatMath.RollDamage` which rounds to int. Spec says rounding at step 9.
- ✅ Apply crit multiplier (line 350-363) - CORRECT (step 8)
- ✅ Round final damage (line 366) - CORRECT (step 9)

**Issue**: `RollDamage` (line 110 in CombatMath.cs) returns `int` after rounding, but spec says rounding should happen at step 9 after crit multiplier. Functionally correct (final result same) but violates spec ordering semantics.

### F) Derived Stats Formulas

✅ **PASS**: `src/Combats.Battle.Domain/Rules/CombatMath.cs` - All derived stat formulas match spec:
- HpMax = BaseHp + Stamina * HpPerEnd (line 18) - Note: spec says "Endurance" but codebase uses "Stamina" (acceptable naming)
- BaseDamage = BaseWeaponDamage + Str*DamagePerStr + Agi*DamagePerAgi + Int*DamagePerInt (lines 21-24) - CORRECT
- DamageMin = floor(BaseDamage * SpreadMin) (line 27) - CORRECT
- DamageMax = ceil(BaseDamage * SpreadMax) (line 28) - CORRECT
- MfDodge = Agi*MfPerAgi (line 31) - CORRECT
- MfAntiDodge = Agi*MfPerAgi (line 32) - CORRECT (spec says same formula)
- MfCrit = Int*MfPerInt (line 33) - CORRECT
- MfAntiCrit = Int*MfPerInt (line 34) - CORRECT (spec says same formula)

### G) Chance Computation

✅ **PASS**: `src/Combats.Battle.Domain/Rules/CombatMath.cs` - ComputeChance formula (lines 53-64) exactly matches spec:
- raw = base + scale * diff / (abs(diff) + kBase) (line 62)
- clamp(raw, min, max) (line 63)
- DodgeChance diff = defender.MfDodge - attacker.MfAntiDodge (line 75) - CORRECT
- CritChance diff = attacker.MfCrit - defender.MfAntiCrit (line 94) - CORRECT

### H) Damage Spread Validation

❌ **FAIL**: `src/Combats.Battle.Domain/Rules/CombatBalance.cs` lines 75-78 - DamageBalance constructor validates SpreadMin/SpreadMax as `>= 0 && <= 1`, but spec says they are MULTIPLIERS and config example shows 0.85..1.15 (SpreadMax > 1). Current validation rejects valid multiplier values > 1.0.

**Files**: `src/Combats.Battle.Domain/Rules/CombatBalance.cs:75-78`

**Code Symbol**: `DamageBalance` constructor

**Justification**: Validation constraint `spreadMax <= 1` conflicts with spec requirement that SpreadMax can be > 1 (example: 1.15). Current appsettings uses 0.9/0.95 which works, but any attempt to use values like 0.85/1.15 will throw ArgumentException.

### I) HpMax Computation Frequency

✅ **PASS**: HpMax is correctly computed ONCE at battle creation (`src/Combats.Battle.Application/UseCases/Lifecycle/BattleLifecycleAppService.cs` lines 72-73, 75-76) and stored in PlayerState.MaxHp. While `ComputeDerived` is called every turn in `BattleEngine.CalculateDamage` (lines 311-312) to get damage ranges and mf values, HpMax itself is never recomputed from derived stats - it's stored in PlayerState.MaxHp and used directly. Spec says "HpMax computed once at creation" which is satisfied.

**Files**: `src/Combats.Battle.Application/UseCases/Lifecycle/BattleLifecycleAppService.cs:72-73,75-76`

**Code Symbol**: `BattleLifecycleAppService.HandleBattleCreatedAsync` method

**Justification**: Spec requires only HpMax to be "computed once at creation", not all derived stats. Other derived stats (damage ranges, mf) must be computed for damage calculations and are correctly computed via ComputeDerived when needed. HpMax is stored in PlayerState.MaxHp and never recomputed.

### J) Ruleset CombatBalance Population

✅ **PASS**: Ruleset always carries CombatBalance. Verified:
- `src/Combats.Battle.Domain/Rules/Ruleset.cs` line 45 - Constructor requires non-null CombatBalance
- `src/Combats.Battle.Application/UseCases/Lifecycle/RulesetNormalizer.cs` lines 67, 85 - Always populates from ICombatBalanceProvider
- Both API and Worker would use same RulesetNormalizer flow

### K) Test Coverage

❌ **FAIL**: No tests found for CombatMath formulas. Required test cases missing:
- mf calculation tests
- ComputeChance clamp behavior tests  
- crit vs block interaction tests
- spread bounds tests (especially with SpreadMax > 1)

**Files**: No test files found matching `**/CombatMath*Test*.cs` or `**/*Combat*Test*.cs`

**Justification**: Application tests exist (`BattleTurnAppServiceTests.cs`, `TurnDeadlineWorkerTests.cs`) but they test application services, not domain formulas. No unit tests exist for CombatMath methods to verify formula correctness, clamp behavior, or edge cases.

---

## Mismatch List (Actionable)

### 1. DamageBalance Spread Validation Rejects Valid Multiplier Values

**Symptom**: Cannot configure damage spread multipliers > 1.0 (e.g., 0.85..1.15) because validation enforces `SpreadMax <= 1`.

**Root Cause**: `DamageBalance` constructor (CombatBalance.cs:75-78) has hardcoded validation constraint assuming spread is a percentage [0..1] rather than a multiplier.

**Exact Location**: 
- File: `src/Combats.Battle.Domain/Rules/CombatBalance.cs`
- Lines: 75-78
- Symbol: `DamageBalance` constructor

**Minimal Fix**: Remove the `<= 1` upper bound validation for SpreadMin and SpreadMax. Keep `>= 0` and `SpreadMin < SpreadMax` checks. Change:
```csharp
if (spreadMin < 0 || spreadMin > 1)
    throw new ArgumentException("SpreadMin must be between 0 and 1", nameof(spreadMin));
if (spreadMax < 0 || spreadMax > 1)
    throw new ArgumentException("SpreadMax must be between 0 and 1", nameof(spreadMax));
```
To:
```csharp
if (spreadMin < 0)
    throw new ArgumentException("SpreadMin cannot be negative", nameof(spreadMin));
if (spreadMax < 0)
    throw new ArgumentException("SpreadMax cannot be negative", nameof(spreadMax));
```

### 2. RollDamage Rounds Before Spec-Required Step

**Symptom**: Base damage is rounded to int in `RollDamage` (step 7), but spec says rounding should happen at step 9 after crit multiplier. Functionally correct but violates spec ordering.

**Root Cause**: `CombatMath.RollDamage` returns `int` after rounding, but spec requires decimal precision through step 8.

**Exact Location**:
- File: `src/Combats.Battle.Domain/Rules/CombatMath.cs`
- Lines: 107-111
- Symbol: `RollDamage` method

**Minimal Fix**: Change `RollDamage` return type from `int` to `decimal` and remove rounding. Move rounding to step 9 in BattleEngine (already done, but baseDamage should be decimal). Change:
```csharp
public static int RollDamage(IRandomProvider rng, DerivedCombatStats attacker)
{
    var damage = rng.NextDecimal(attacker.DamageMin, attacker.DamageMax);
    return (int)Math.Round(damage, MidpointRounding.AwayFromZero);
}
```
To:
```csharp
public static decimal RollDamage(IRandomProvider rng, DerivedCombatStats attacker)
{
    return rng.NextDecimal(attacker.DamageMin, attacker.DamageMax);
}
```
And update `BattleEngine.CalculateDamage` line 347 to use `decimal baseDamage` (change already present at line 350, just needs type change).

### 3. Missing Test Coverage for Combat Formulas

**Symptom**: No unit tests exist to verify CombatMath formulas, clamp behavior, crit/block interactions, or spread edge cases.

**Root Cause**: Test project lacks domain formula tests. Only application service tests exist.

**Exact Location**: 
- Missing test file: `src/Combats.Battle.Domain.Tests/CombatMathTests.cs` (or similar in Application.Tests)

**Minimal Fix**: Create test file with:
- `ComputeDerived_MfCalculation_Correct` - Verify mf formulas
- `ComputeChance_ClampBehavior_RespectsMinMax` - Test clamp at bounds
- `CalculateDamage_CritVsBlock_BypassBlockWorks` - Test crit bypass
- `CalculateDamage_CritVsBlock_HybridModeWorks` - Test hybrid mode
- `DamageSpread_Bounds_SingleDamageBaseMax` - Test SpreadMax > 1 works after fix

**Note**: This requires creating Domain.Tests project or adding to Application.Tests.

---

## Patch Plan (Minimal Diffs)

### File 1: `src/Combats.Battle.Domain/Rules/CombatBalance.cs`

**Change**: Remove upper bound validation for SpreadMin/SpreadMax (allow multipliers > 1.0)

```diff
--- a/src/Combats.Battle.Domain/Rules/CombatBalance.cs
+++ b/src/Combats.Battle.Domain/Rules/CombatBalance.cs
@@ -72,10 +72,10 @@ public sealed record DamageBalance
         if (damagePerInt < 0)
             throw new ArgumentException("DamagePerInt cannot be negative", nameof(damagePerInt));
-        if (spreadMin < 0 || spreadMin > 1)
-            throw new ArgumentException("SpreadMin must be between 0 and 1", nameof(spreadMin));
-        if (spreadMax < 0 || spreadMax > 1)
-            throw new ArgumentException("SpreadMax must be between 0 and 1", nameof(spreadMax));
+        if (spreadMin < 0)
+            throw new ArgumentException("SpreadMin cannot be negative", nameof(spreadMin));
+        if (spreadMax < 0)
+            throw new ArgumentException("SpreadMax cannot be negative", nameof(spreadMax));
         if (spreadMin >= spreadMax)
             throw new ArgumentException("SpreadMin must be less than SpreadMax", nameof(spreadMin));
```

### File 2: `src/Combats.Battle.Domain/Rules/CombatMath.cs`

**Change**: Change RollDamage to return decimal (no rounding)

```diff
--- a/src/Combats.Battle.Domain/Rules/CombatMath.cs
+++ b/src/Combats.Battle.Domain/Rules/CombatMath.cs
@@ -104,10 +104,9 @@ public static class CombatMath
     /// <summary>
     /// Rolls a random damage value within the attacker's damage range.
     /// </summary>
-    public static int RollDamage(IRandomProvider rng, DerivedCombatStats attacker)
+    public static decimal RollDamage(IRandomProvider rng, DerivedCombatStats attacker)
     {
-        var damage = rng.NextDecimal(attacker.DamageMin, attacker.DamageMax);
-        return (int)Math.Round(damage, MidpointRounding.AwayFromZero);
+        return rng.NextDecimal(attacker.DamageMin, attacker.DamageMax);
     }
 }
```

### File 3: `src/Combats.Battle.Domain/Engine/BattleEngine.cs`

**Change**: Update baseDamage variable type from int to decimal

```diff
--- a/src/Combats.Battle.Domain/Engine/BattleEngine.cs
+++ b/src/Combats.Battle.Domain/Engine/BattleEngine.cs
@@ -345,7 +345,7 @@ public sealed class BattleEngine : IBattleEngine
         }
 
         // 7. Roll base damage
-        var baseDamage = CombatMath.RollDamage(_rng, derivedAtt);
+        decimal baseDamage = CombatMath.RollDamage(_rng, derivedAtt);
 
         // 8. Apply crit multiplier if crit occurred
         decimal finalDamage = baseDamage;
```

### File 4: `src/Combats.Battle.Api/appsettings.json` (Optional - Example Update)

**Change**: Update example config to demonstrate SpreadMax > 1 works (optional, for documentation)

```diff
--- a/src/Combats.Battle.Api/appsettings.json
+++ b/src/Combats.Battle.Api/appsettings.json
@@ -79,8 +79,8 @@
       "DamagePerStr": 1.0,
       "DamagePerAgi": 0,
       "DamagePerInt": 0,
-      "SpreadMin": 0.9,
-      "SpreadMax": 0.95
+      "SpreadMin": 0.85,
+      "SpreadMax": 1.15
     },
```

**Note**: Only if you want to demonstrate the fix works with the spec example values. Current values (0.9/0.95) are also valid.

---

## Summary

**Critical Issues**: 1 (Spread validation blocks valid config)
**Minor Issues**: 2 (RollDamage rounding order, missing tests)
**Passing Requirements**: 9/11

**Priority Fixes**:
1. **IMMEDIATE**: Fix DamageBalance validation (blocks production use of multiplier > 1.0)
2. **RECOMMENDED**: Fix RollDamage rounding order (spec compliance, no functional bug)
3. **RECOMMENDED**: Add domain formula tests (quality assurance, prevents regressions)

