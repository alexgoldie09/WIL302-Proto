/// <summary>
/// Implemented by any MonoBehaviour that lives on a specific biome and should
/// be tracked by BiomeManager — flora, fauna, structures, slots, crafting stations.
/// 
/// Registrants call BiomeManager.Instance.RegisterOccupant(this) in OnEnable
/// and BiomeManager.Instance.UnregisterOccupant(this) in OnDisable.
/// </summary>
public interface IBiomeOccupant
{
    /// <summary>
    /// The biome this occupant belongs to.
    /// Set via a serialized field in the Inspector so designers control placement.
    /// </summary>
    BiomeManager.BiomeType HomeBiome { get; }
}