using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Wright;

/// <summary>
/// Byggematerialene til et fly. Byggeplassen noterer nøyaktig hva hvert steg trakk ut av hotbaren (tresort, metall
/// og alt), lista følger med over i flyet ved utrulling, og demontering med skiftenøkkel gir alt tilbake.
/// Lagres som WatchedAttributes["buildMaterials"]: { "0": stack, "1": stack, ... }.
/// </summary>
public static class WrightMaterials
{
    public const string AttrKey = "buildMaterials";

    /// <summary>Hotbaren før et byggesteg: (slot, kopi av stacken) — sammenlignes med etterpå.</summary>
    public static List<(ItemSlot slot, ItemStack? stack)> Snapshot(EntityAgent byEntity)
    {
        var list = new List<(ItemSlot, ItemStack?)>();
        if (byEntity is not EntityPlayer ep || ep.Player == null) return list;
        foreach (var slot in ep.Player.InventoryManager.GetHotbarInventory())
        {
            list.Add((slot, slot.Itemstack?.Clone()));
        }
        return list;
    }

    /// <summary>Det som forsvant fra hotbaren siden snapshotet, lagt til i entitetens materialliste.</summary>
    public static void RecordConsumed(Entity entity, List<(ItemSlot slot, ItemStack? stack)> before)
    {
        var tree = Tree(entity);
        foreach (var (slot, old) in before)
        {
            if (old == null) continue;
            var now = slot.Itemstack;
            int used = now != null && now.Collectible.Code == old.Collectible.Code ? old.StackSize - now.StackSize : old.StackSize;
            if (used <= 0) continue;
            var taken = old.Clone();
            taken.StackSize = used;
            tree[tree.Count.ToString()] = new ItemstackAttribute(taken);
        }
        entity.WatchedAttributes.MarkPathDirty(AttrKey);
    }

    public static void CopyTo(Entity from, Entity to)
    {
        if (from.WatchedAttributes[AttrKey] is ITreeAttribute tree)
        {
            to.WatchedAttributes[AttrKey] = tree.Clone();
        }
    }

    /// <summary>Lagrede materialer, eller null hvis entiteten er eldre enn denne lista (fly bygd før demontering fantes).</summary>
    public static List<ItemStack>? Stored(Entity entity)
    {
        if (entity.WatchedAttributes[AttrKey] is not ITreeAttribute tree) return null;
        var list = new List<ItemStack>();
        foreach (var pair in tree)
        {
            var stack = (pair.Value as ItemstackAttribute)?.value;
            if (stack == null || !stack.ResolveBlockOrItem(entity.World)) continue;
            list.Add(stack);
        }
        return list;
    }

    /// <summary>
    /// Fallback for gamle fly: regn ut materialene fra byggestegene i byggeplassens JSON. Jokertegn (tresort, metall)
    /// blir eik/jern, ellers første variant som finnes.
    /// </summary>
    public static List<ItemStack> FromStages(IWorldAccessor world, string constructionCode)
    {
        var list = new List<ItemStack>();
        var type = world.GetEntityType(new AssetLocation(constructionCode));
        var stages = type?.Attributes?["stages"];
        if (stages == null || !stages.Exists) return list;
        foreach (var stage in stages.AsArray() ?? new JsonObject[0])
        {
            foreach (var req in stage["requireStacks"].AsArray() ?? new JsonObject[0])
            {
                string code = req["code"].AsString("");
                int qty = req["quantity"].AsInt(1);
                bool block = req["type"].AsString("item") == "block";
                var coll = ResolveWildcard(world, code, block);
                if (coll != null) list.Add(new ItemStack(coll, qty));
            }
        }
        return list;
    }

    static CollectibleObject? ResolveWildcard(IWorldAccessor world, string code, bool block)
    {
        if (!code.Contains('*')) return Get(world, new AssetLocation(code), block);
        foreach (var guess in new[] { "oak", "iron" })
        {
            var c = Get(world, new AssetLocation(code.Replace("*", guess)), block);
            if (c != null) return c;
        }
        var loc = new AssetLocation(code);
        if (block)
        {
            var found = world.SearchBlocks(loc);
            return found.Length > 0 ? found[0] : null;
        }
        var items = world.SearchItems(loc);
        return items.Length > 0 ? items[0] : null;
    }

    static CollectibleObject? Get(IWorldAccessor world, AssetLocation loc, bool block)
    {
        return block ? world.GetBlock(loc) : world.GetItem(loc);
    }

    /// <summary>Gi stackene til spilleren; det som ikke får plass faller på bakken ved entiteten.</summary>
    public static void GiveBack(EntityAgent byEntity, IEnumerable<ItemStack> stacks, Vec3d dropAt)
    {
        foreach (var stack in stacks)
        {
            if (!byEntity.TryGiveItemStack(stack.Clone()))
            {
                byEntity.World.SpawnItemEntity(stack, dropAt);
            }
        }
    }

    /// <summary>Sant hvis gjenstanden matcher en av kodene i <c>attributes.disassemblyTools</c> (jokertegn tillatt).</summary>
    public static bool IsDisassemblyTool(ItemStack? stack, JsonObject attributes)
    {
        if (stack == null) return false;
        var codes = attributes["disassemblyTools"].AsArray<string>(null);
        if (codes == null) return false;
        foreach (var code in codes)
        {
            if (WildcardUtil.Match(new AssetLocation(code), stack.Collectible.Code)) return true;
        }
        return false;
    }

    /// <summary>Skiftenøklene fra <c>attributes.disassemblyTools</c>, til hjelpeteksten ved musepekeren.</summary>
    public static ItemStack[] ToolStacks(IWorldAccessor world, JsonObject attributes)
    {
        var list = new List<ItemStack>();
        foreach (var code in attributes["disassemblyTools"].AsArray<string>(new string[0]))
        {
            var item = world.GetItem(new AssetLocation(code));
            if (item != null) list.Add(new ItemStack(item));
        }
        return list.ToArray();
    }

    public static void Error(Entity entity, EntityAgent byEntity, string langCode)
    {
        if (entity.Api is ICoreClientAPI capi && byEntity == capi.World.Player.Entity)
        {
            capi.TriggerIngameError(entity, "wrightdisassemble", Vintagestory.API.Config.Lang.Get(langCode));
        }
    }

    static ITreeAttribute Tree(Entity entity)
    {
        if (entity.WatchedAttributes[AttrKey] is ITreeAttribute tree) return tree;
        var created = new TreeAttribute();
        entity.WatchedAttributes[AttrKey] = created;
        return created;
    }
}
