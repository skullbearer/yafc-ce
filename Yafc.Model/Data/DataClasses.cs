﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Yafc.UI;
[assembly: InternalsVisibleTo("Yafc.Parser")]

namespace Yafc.Model;

public interface IFactorioObjectWrapper {
    string text { get; }
    FactorioObject target { get; }
    double amount { get; }
}

internal enum FactorioObjectSortOrder {
    SpecialGoods,
    Items,
    Fluids,
    Recipes,
    Mechanics,
    Technologies,
    Entities
}

public enum FactorioId { }

public abstract class FactorioObject : IFactorioObjectWrapper, IComparable<FactorioObject> {
    public string? factorioType { get; internal set; }
    public string name { get; internal set; } = null!; // null-forgiving: Initialized to non-null by GetObject.
    public string typeDotName => type + '.' + name;
    public string locName { get; internal set; } = null!; // null-forgiving: Copied from name if still null at the end of CalculateMaps
    public string? locDescr { get; internal set; }
    public FactorioIconPart[]? iconSpec { get; internal set; }
    public Icon icon { get; internal set; }
    public FactorioId id { get; internal set; }
    internal abstract FactorioObjectSortOrder sortingOrder { get; }
    public FactorioObjectSpecialType specialType { get; internal set; }
    public abstract string type { get; }
    FactorioObject IFactorioObjectWrapper.target => this;
    double IFactorioObjectWrapper.amount => 1f;

    string IFactorioObjectWrapper.text => locName;

    public void FallbackLocalization(FactorioObject? other, string description) {
        if (locName == null) {
            if (other == null) {
                locName = name;
            }
            else {
                locName = other.locName;
                locDescr = description + " " + locName;
            }
        }

        if (iconSpec == null && other?.iconSpec != null) {
            iconSpec = other.iconSpec;
        }
    }

    public abstract void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp);

    public override string ToString() => name;

    public int CompareTo(FactorioObject? other) => DataUtils.DefaultOrdering.Compare(this, other);

    public virtual bool showInExplorers => true;
}

public class FactorioIconPart(string path) {
    public string path = path;
    public float size = 32;
    public float x, y, r = 1, g = 1, b = 1, a = 1;
    public float scale = 1;

    public bool IsSimple() => x == 0 && y == 0 && r == 1 && g == 1 && b == 1 && a == 1 && scale == 1;
}

[Flags]
public enum RecipeFlags {
    UsesMiningProductivity = 1 << 0,
    UsesFluidTemperature = 1 << 2,
    ScaleProductionWithPower = 1 << 3,
    LimitedByTickRate = 1 << 4,
}

public abstract class RecipeOrTechnology : FactorioObject {
    public EntityCrafter[] crafters { get; internal set; } = null!; // null-forgiving: Initialized by CalculateMaps
    public Ingredient[] ingredients { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    public Product[] products { get; internal set; } = null!; // null-forgiving: Initialized by LoadRecipeData, LoadTechnologyData, and after all calls to CreateSpecialRecipe
    public Module[] modules { get; internal set; } = [];
    public Entity? sourceEntity { get; internal set; }
    public Goods? mainProduct { get; internal set; }
    public double time { get; internal set; }
    public bool enabled { get; internal set; }
    public bool hidden { get; internal set; }
    public RecipeFlags flags { get; internal set; }
    public override string type => "Recipe";

    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Recipes;

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (ingredients.Length > 0) {
            temp.Clear();
            foreach (var ingredient in ingredients) {
                if (ingredient.variants != null) {
                    collector.Add(ingredient.variants, DependencyList.Flags.IngredientVariant);
                }
                else {
                    temp.Add(ingredient.goods);
                }
            }
            if (temp.Count > 0) {
                collector.Add(temp, DependencyList.Flags.Ingredient);
            }
        }
        collector.Add(crafters, DependencyList.Flags.CraftingEntity);
        if (sourceEntity != null) {
            collector.Add(new[] { sourceEntity.id }, DependencyList.Flags.SourceEntity);
        }
    }

    public bool CanFit(int itemInputs, int fluidInputs, Goods[]? slots) {
        foreach (var ingredient in ingredients) {
            if (ingredient.goods is Item && --itemInputs < 0) {
                return false;
            }

            if (ingredient.goods is Fluid && --fluidInputs < 0) {
                return false;
            }

            if (slots != null && !slots.Contains(ingredient.goods)) {
                return false;
            }
        }
        return true;
    }

    public virtual bool CanAcceptModule(Item _) => true;
}

public enum FactorioObjectSpecialType {
    Normal,
    Voiding,
    Barreling,
    Stacking,
    Pressurization,
    Crating,
}

public class Recipe : RecipeOrTechnology {
    public Technology[] technologyUnlock { get; internal set; } = [];
    public bool HasIngredientVariants() {
        foreach (var ingredient in ingredients) {
            if (ingredient.variants != null) {
                return true;
            }
        }

        return false;
    }

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        base.GetDependencies(collector, temp);

        if (!enabled) {
            collector.Add(technologyUnlock, DependencyList.Flags.TechnologyUnlock);
        }
    }

    public bool IsProductivityAllowed() {
        foreach (var module in modules) {
            if (module.moduleSpecification.productivity != 0f) {
                return true;
            }
        }

        return false;
    }

    public override bool CanAcceptModule(Item module) => modules.Contains(module);
}

public class Mechanics : Recipe {
    internal FactorioObject source { get; set; } = null!; // null-forgiving: Set by CreateSpecialRecipe
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Mechanics;
    public override string type => "Mechanics";
}

public class Ingredient : IFactorioObjectWrapper {
    public readonly double amount;
    public Goods goods { get; internal set; }
    public Goods[]? variants { get; internal set; }
    public TemperatureRange temperature { get; internal set; } = TemperatureRange.Any;
    public Ingredient(Goods goods, double amount) {
        this.goods = goods;
        this.amount = amount;
        if (goods is Fluid fluid) {
            temperature = fluid.temperatureRange;
        }
    }

    string IFactorioObjectWrapper.text {
        get {
            string text = goods.locName;
            if (amount != 1f) {
                text = amount + "x " + text;
            }

            if (!temperature.IsAny()) {
                text += " (" + temperature + ")";
            }

            return text;
        }
    }

    FactorioObject IFactorioObjectWrapper.target => goods;

    double IFactorioObjectWrapper.amount => amount;

    public bool ContainsVariant(Goods product) {
        if (goods == product) {
            return true;
        }

        if (variants != null) {
            return Array.IndexOf(variants, product) >= 0;
        }

        return false;
    }
}

public class Product : IFactorioObjectWrapper {
    public readonly Goods goods;
    internal readonly double amountMin;
    internal readonly double amountMax;
    internal readonly double probability;
    public readonly double amount; // This is average amount including probability and range
    internal double productivityAmount { get; private set; }

    public void SetCatalyst(double catalyst) {
        double catalyticMin = amountMin - catalyst;
        double catalyticMax = amountMax - catalyst;

        if (catalyticMax <= 0) {
            productivityAmount = 0f;
        }
        else if (catalyticMin >= 0f) {
            productivityAmount = (catalyticMin + catalyticMax) * 0.5f * probability;
        }
        else {
            // TODO super duper rare case, might not be precise
            productivityAmount = probability * catalyticMax * catalyticMax * 0.5f / (catalyticMax - catalyticMin);
        }
    }

    internal double GetAmountForRow(RecipeRow row) => GetAmountPerRecipe(row.parameters.productivity) * (double)row.recipesPerSecond;
    internal double GetAmountPerRecipe(double productivityBonus) => amount + (productivityBonus * productivityAmount);

    public Product(Goods goods, double amount) {
        this.goods = goods;
        amountMin = amountMax = this.amount = productivityAmount = amount;
        probability = 1f;
    }

    public Product(Goods goods, double min, double max, double probability) {
        this.goods = goods;
        amountMin = min;
        amountMax = max;
        this.probability = probability;
        amount = productivityAmount = probability * (min + max) / 2;
    }

    public bool IsSimple => amountMin == amountMax && probability == 1f;

    FactorioObject IFactorioObjectWrapper.target => goods;

    string IFactorioObjectWrapper.text {
        get {
            string text = goods.locName;

            if (amountMin != 1f || amountMax != 1f) {
                text = DataUtils.FormatAmount(amountMax, UnitOfMeasure.None) + "x " + text;

                if (amountMin != amountMax) {
                    text = DataUtils.FormatAmount(amountMin, UnitOfMeasure.None) + "-" + text;
                }
            }
            if (probability != 1f) {
                text = DataUtils.FormatAmount(probability, UnitOfMeasure.Percent) + " " + text;
            }

            return text;
        }
    }
    double IFactorioObjectWrapper.amount => amount;
}

// Abstract base for anything that can be produced or consumed by recipes (etc)
public abstract class Goods : FactorioObject {
    public double fuelValue { get; internal set; }
    public abstract bool isPower { get; }
    public Fluid? fluid => this as Fluid;
    public Recipe[] production { get; internal set; } = [];
    public Recipe[] usages { get; internal set; } = [];
    public FactorioObject[] miscSources { get; internal set; } = [];
    public Entity[] fuelFor { get; internal set; } = [];
    public abstract UnitOfMeasure flowUnitOfMeasure { get; }

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) => collector.Add(production.Concat(miscSources).ToArray(), DependencyList.Flags.Source);

    public virtual bool HasSpentFuel([MaybeNullWhen(false)] out Item spent) {
        spent = null;
        return false;
    }
}

public class Item : Goods {
    public Item? fuelResult { get; internal set; }
    public int stackSize { get; internal set; }
    public Entity? placeResult { get; internal set; }
    public override bool isPower => false;
    public override string type => "Item";
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Items;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.ItemPerSecond;

    public override bool HasSpentFuel([NotNullWhen(true)] out Item? spent) {
        spent = fuelResult;
        return spent != null;
    }
}

public class Module : Item {
    public ModuleSpecification moduleSpecification { get; internal set; } = null!; // null-forgiving: Initialized by DeserializeItem.
}

public class Fluid : Goods {
    public override string type => "Fluid";
    public string originalName { get; internal set; } = null!; // name without temperature, null-forgiving: Initialized by DeserializeFluid.
    public double heatCapacity { get; internal set; } = 1e-3f;
    public TemperatureRange temperatureRange { get; internal set; }
    public int temperature { get; internal set; }
    public double heatValue { get; internal set; }
    public List<Fluid>? variants { get; internal set; }
    public override bool isPower => false;
    public override UnitOfMeasure flowUnitOfMeasure => UnitOfMeasure.FluidPerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Fluids;
    internal Fluid Clone() => (Fluid)MemberwiseClone();

    internal void SetTemperature(int temp) {
        temperature = temp;
        heatValue = (temp - temperatureRange.min) * heatCapacity;
    }
}

public class Special : Goods {
    internal string? virtualSignal { get; set; }
    internal bool power;
    internal bool isResearch;
    public override bool isPower => power;
    public override string type => isPower ? "Power" : "Special";
    public override UnitOfMeasure flowUnitOfMeasure => isPower ? UnitOfMeasure.Megawatt : UnitOfMeasure.PerSecond;
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.SpecialGoods;
    public override bool showInExplorers => !isResearch;

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (isResearch) {
            collector.Add(Database.technologies.all.ToArray(), DependencyList.Flags.Source);
        }
        else {
            base.GetDependencies(collector, temp);
        }
    }
}

[Flags]
public enum AllowedEffects {
    Speed = 1 << 0,
    Productivity = 1 << 1,
    Consumption = 1 << 2,
    Pollution = 1 << 3,

    All = Speed | Productivity | Consumption | Pollution,
    None = 0
}

public class Entity : FactorioObject {
    public Product[] loot { get; internal set; } = [];
    public bool mapGenerated { get; internal set; }
    public double mapGenDensity { get; internal set; }
    public double power { get; internal set; }
    public EntityEnergy energy { get; internal set; } = null!; // TODO: Prove that this is always properly initialized. (Do we need an EntityWithEnergy type?)
    public Item[] itemsToPlace { get; internal set; } = null!; // null-forgiving: This is initialized in CalculateMaps.
    public int size { get; internal set; }
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Entities;
    public override string type => "Entity";

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        if (energy != null) {
            collector.Add(energy.fuels, DependencyList.Flags.Fuel);
        }

        if (mapGenerated) {
            return;
        }

        collector.Add(itemsToPlace, DependencyList.Flags.ItemToPlace);
    }
}

public abstract class EntityWithModules : Entity {
    public AllowedEffects allowedEffects { get; internal set; } = AllowedEffects.None;
    public int moduleSlots { get; internal set; }

    public static bool CanAcceptModule(ModuleSpecification module, AllowedEffects effects) {
        // Check most common cases first
        if (effects == AllowedEffects.All) {
            return true;
        }

        if (effects == (AllowedEffects.Consumption | AllowedEffects.Pollution | AllowedEffects.Speed)) {
            return module.productivity == 0f;
        }

        if (effects == AllowedEffects.None) {
            return false;
        }
        // Check the rest
        if (module.productivity != 0f && (effects & AllowedEffects.Productivity) == 0) {
            return false;
        }

        if (module.consumption != 0f && (effects & AllowedEffects.Consumption) == 0) {
            return false;
        }

        if (module.pollution != 0f && (effects & AllowedEffects.Pollution) == 0) {
            return false;
        }

        if (module.speed != 0f && (effects & AllowedEffects.Speed) == 0) {
            return false;
        }

        return true;
    }

    public bool CanAcceptModule(ModuleSpecification module) => CanAcceptModule(module, allowedEffects);
}

public class EntityCrafter : EntityWithModules {
    public int itemInputs { get; internal set; }
    public int fluidInputs { get; internal set; } // fluid inputs for recipe, not including power
    public Goods[]? inputs { get; internal set; }
    public RecipeOrTechnology[] recipes { get; internal set; } = null!; // null-forgiving: Set in the first step of CalculateMaps
    private double _craftingSpeed = 1;
    public double craftingSpeed {
        // The speed of a lab is baseSpeed * (1 + researchSpeedBonus) * Math.Min(0.2, 1 + moduleAndBeaconSpeedBonus)
        get => _craftingSpeed * (1 + (factorioType == "lab" ? Project.current.settings.researchSpeedBonus : 0));
        internal set => _craftingSpeed = value;
    }
    public float productivity { get; internal set; }
}

public class EntityInserter : Entity {
    public bool isStackInserter { get; internal set; }
    public double inserterSwingTime { get; internal set; }
}

public class EntityAccumulator : Entity {
    public double accumulatorCapacity { get; internal set; }
}

public class EntityBelt : Entity {
    public double beltItemsPerSecond { get; internal set; }
}

public class EntityReactor : EntityCrafter {
    public double reactorNeighborBonus { get; internal set; }
}

public class EntityBeacon : EntityWithModules {
    public double beaconEfficiency { get; internal set; }
}

public class EntityContainer : Entity {
    public int inventorySize { get; internal set; }
    public string? logisticMode { get; set; }
    public int logisticSlotsCount { get; internal set; }
}

public class Technology : RecipeOrTechnology { // Technology is very similar to recipe
    public double count { get; internal set; } // TODO support formula count
    public Technology[] prerequisites { get; internal set; } = [];
    public Recipe[] unlockRecipes { get; internal set; } = [];
    internal override FactorioObjectSortOrder sortingOrder => FactorioObjectSortOrder.Technologies;
    public override string type => "Technology";

    public override void GetDependencies(IDependencyCollector collector, List<FactorioObject> temp) {
        base.GetDependencies(collector, temp);
        if (prerequisites.Length > 0) {
            collector.Add(prerequisites, DependencyList.Flags.TechnologyPrerequisites);
        }

        if (hidden && !enabled) {
            collector.Add(Array.Empty<FactorioId>(), DependencyList.Flags.Hidden);
        }
    }
}

public enum EntityEnergyType {
    Void,
    Electric,
    Heat,
    SolidFuel,
    FluidFuel,
    FluidHeat,
    Labor, // Special energy type for character
}

public class EntityEnergy {
    public EntityEnergyType type { get; internal set; }
    public TemperatureRange workingTemperature { get; internal set; }
    public TemperatureRange acceptedTemperature { get; internal set; } = TemperatureRange.Any;
    public double emissions { get; internal set; }
    public double drain { get; internal set; }
    public double fuelConsumptionLimit { get; internal set; } = double.PositiveInfinity;
    public Goods[] fuels { get; internal set; } = [];
    public double effectivity { get; internal set; } = 1f;
}

public class ModuleSpecification {
    public double consumption { get; internal set; }
    public double speed { get; internal set; }
    public float productivity { get; internal set; }
    public double pollution { get; internal set; }
    public Recipe[]? limitation { get; internal set; }
    public Recipe[]? limitation_blacklist { get; internal set; }
}

public struct TemperatureRange(int min, int max) {
    public int min = min;
    public int max = max;

    public static readonly TemperatureRange Any = new TemperatureRange(int.MinValue, int.MaxValue);
    public readonly bool IsAny() => min == int.MinValue && max == int.MaxValue;

    public readonly bool IsSingle() => min == max;

    public TemperatureRange(int single) : this(single, single) { }

    public override readonly string ToString() {
        if (min == max) {
            return min + "°";
        }

        return min + "°-" + max + "°";
    }

    public readonly bool Contains(int value) => min <= value && max >= value;
}
