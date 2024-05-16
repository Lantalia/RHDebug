using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using HarmonyLib;


namespace RHDebug {
    [HarmonyPatch]
    public class RHDebugModSystem : ModSystem
    {
        public static int last_message_time_projectile = 0;
        public static int last_message_time_basket_trap = 0;
        public static int last_message_time_creature_diet = 0;
        public static int last_message_time_micro = 0;
        public static int message_delta = 30;
        public static bool fixup_broken = true;
        public static bool skip_broken = false;
        public static bool fake_one = false;
        public static bool dump_projectile = false;
        public static bool dump_trap = false;
        public static bool dump_diet = false;
        public static bool dump_micro = false;
        public static ICoreServerAPI api;
        public Harmony harmony;
        public static String ArrayFormat(Object[] array)
        {
            return array == null ? "null" : "[" + String.Join(",", array) + "]";
        }
        public static String DietFormat(CreatureDiet __instance)
        {
            return String.Format("({0}; {1}; {2})",
                ArrayFormat(__instance.FoodCategories == null ? null : Array.ConvertAll<EnumFoodCategory, String>(__instance.FoodCategories, x => x.ToString())),
                ArrayFormat(__instance.FoodTags),
                ArrayFormat(__instance.SkipFoodTags));
        }
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            RHDebugModSystem.api = api;
            if (!Harmony.HasAnyPatches(Mod.Info.ModID))
            {
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll(typeof(RHDebugModSystem).Assembly);
            }
            base.StartServerSide(api);
            // Placeholder to prove it is working
            api.ChatCommands.Create("rhdebug")
                .WithDescription("server debug controls")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ =>
                {
                    return TextCommandResult.Success("Status " +
                    (fake_one ? "breaking next projectile " : "") +
                    (skip_broken ? "skipping broken projectiles " : "") +
                    (fixup_broken ? "fixing up broken projectiles" : ""));
                })
                .BeginSubCommand("fake")
                    .WithDescription("Break the next projectile")
                    .HandleWith(_ => { fake_one = true; return TextCommandResult.Success(); })
                .EndSubCommand()
                .BeginSubCommand("skip")
                    .WithDescription("Toggle skipping broken projectiles")
                    .HandleWith(_ => { skip_broken = !skip_broken; return TextCommandResult.Success(skip_broken ? "on" : "off"); })
                .EndSubCommand()
                .BeginSubCommand("fixup")
                    .WithDescription("Toggle skipping broken projectiles")
                    .HandleWith(_ => { fixup_broken = !fixup_broken; return TextCommandResult.Success(fixup_broken ? "on" : "off"); })
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Dump the next projectile,basket,diet")
                    .HandleWith(_ => { dump_projectile = dump_trap = dump_diet = dump_micro = true; return TextCommandResult.Success(); })
                    .BeginSubCommand("projectile")
                        .WithDescription("Dump the next projectile")
                        .HandleWith(_ => { dump_projectile = true; return TextCommandResult.Success(); })
                    .EndSubCommand()
                    .BeginSubCommand("trap")
                        .WithDescription("Dump the next basket trap isSuitableFor")
                        .HandleWith(_ => { dump_trap = true; return TextCommandResult.Success(); })
                    .EndSubCommand()
                    .BeginSubCommand("diet")
                        .WithDescription("Dump the next creature diet Matches")
                        .HandleWith(_ => { dump_diet = true; return TextCommandResult.Success(); })
                    .EndSubCommand()
                    .BeginSubCommand("micro")
                        .WithDescription("Dump the next micro block sound interaction")
                        .HandleWith(_ => { dump_micro = true; return TextCommandResult.Success(); })
                    .EndSubCommand()
                .EndSubCommand()
;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
        }


        [HarmonyPatch(typeof(EntityProjectile), "ToBytes")]
        class PatchEntityProjectile
        {
            public static bool Prefix(EntityProjectile __instance, out bool __state, BinaryWriter writer, bool forClient)
            {
                if (dump_projectile)
                {
                    api.Logger.Event("RHDebug dumping a projectile {0} ({1})\n\twith ProjectileStack {2} ({3})\n\tfired by {4} ({5})\n\tat{6}", __instance.Code, __instance.ToString(),
                            __instance.ProjectileStack?.Class, __instance.ProjectileStack?.ToString(),
                            __instance.FiredBy?.GetName(), __instance.FiredBy?.ToString(),
                            __instance.ServerPos);
                    dump_projectile = false;
                }
                if (fake_one)
                {
                    api.Logger.Event("RHDebug breaking a projectile");
                    __instance.ProjectileStack = null; // new ItemStack();
                    fake_one = false;
                }
                __state = false;
                if (__instance?.ProjectileStack == null)
                {
                    int servertime = api.Server.ServerUptimeSeconds;
                    if (last_message_time_projectile + message_delta <= servertime)
                    {
                        api.Logger.Event("Null ProjectileStack in EntityProjectile.ToBytes for {0} firedby {1} at {2}", __instance.Code, __instance.FiredBy?.GetName(), __instance.ServerPos);
                        last_message_time_projectile = servertime;
                    }

                    if (fixup_broken)
                    {
                        __state = true;
                        __instance.ProjectileStack = new ItemStack(); //provide a temporary dummy itemstack to avoid blowing up?
                    }

                    if (skip_broken)
                    {
                        return false;
                    }
                }
                return true;
            }

            public static void Postfix(EntityProjectile __instance, bool __state, BinaryWriter writer, bool forClient)
            {
                if (__state) // We did a fixup, so, we need to purge the projectile
                {
                    __instance.Die();
                }
            }

        }

        [HarmonyPatch(typeof(CreatureDiet), "Matches",new Type[] {typeof(ItemStack)})]
        class PatchCreatureDiet
        {
            public static bool Prefix(CreatureDiet __instance, ref bool __result, ItemStack itemstack)
            {
                if (dump_diet)
                {
                    dump_diet = false;
                    api.Logger.Event("Dump in CreatureDiet.Matches(ItemStack) {0} when checking diet {1}",
                        itemstack?.GetName(),
                        DietFormat(__instance));
                }
                if (itemstack?.Collectible == null) {
                    int servertime = api.Server.ServerUptimeSeconds;
                    if (last_message_time_creature_diet + message_delta <= servertime)
                    {
                        api.Logger.Event("Invalid itemstack in CreatureDiet.Matches(ItemStack) {0} when checking diet {1}", 
                            itemstack?.GetName(),
                            DietFormat(__instance));
                        last_message_time_creature_diet = servertime;

                    }
                    __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(BlockEntityBasketTrap), "IsSuitableFor")]
        class PatchBlockEntityBasketTrap
        {
            public static bool Prefix(BlockEntityBasketTrap __instance, ref bool __result, Entity entity, CreatureDiet diet) {
                if (__instance.TrapState != EnumTrapState.Ready) { __result = false; return false; }
                if (__instance.Inventory[0]?.Itemstack?.Collectible == null)
                {
                    int servertime = api.Server.ServerUptimeSeconds;
                    if (last_message_time_basket_trap + message_delta <= servertime)
                    {
                        api.Logger.Event("Invalid state in BlockEntityBasketTrap.IsSuitableFor itemstack ({0}) not collectible at {1} when checking entity {2} at {3}",
                           __instance.Inventory[0]?.Itemstack?.GetName(), __instance.Pos,
                           entity?.Code, entity?.ServerPos);
                        last_message_time_basket_trap = servertime;
                    }
                    __result = false; return false;
                }
                if (diet == null)
                {
                    int servertime = api.Server.ServerUptimeSeconds;
                    if (last_message_time_basket_trap + message_delta <= servertime)
                    {
                        api.Logger.Event("Null CreatureDiet in BlockEntityBasketTrap.IsSuitableFor when checking entity {0} at {1}",
                           entity?.Code, entity?.ServerPos);
                        last_message_time_basket_trap = servertime;
                    }

                    __result = false; return false;
                }
                if (dump_trap) {
                    dump_trap = false;
                    api.Logger.Event("Dump in BlockEntityBasketTrap.IsSuitableFor itemstack ({0}) at {1} when checking entity {2} at {3}",
                        __instance.Inventory[0]?.Itemstack?.GetName(), __instance.Pos,
                        entity?.Code, entity?.ServerPos);
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MicroBlockSounds), "get_block")]
        class PatchMicroBlockSounds
        {
            public static bool Prefix(MicroBlockSounds __instance, ref Block __result)
            {
                var blocks = __instance.be.Api.World.Blocks;
                var block_count = blocks.Count;
                var blockids = __instance.be.BlockIds;
                if (blockids != null)
                {
                    int servertime = api.Server.ServerUptimeSeconds;
                    foreach ( var blockid in blockids )
                    {
                        if (blockid < 0 || blockid >= block_count)
                        {
                            if (last_message_time_micro + message_delta <= servertime)
                            {
                                api.Logger.Event("Invalid blockid {0} in microblock at ({1}) BlockIds [{2}] blockId count is {3}", blockid, __instance.be.Pos, String.Join(", ", blockids), block_count);
                                last_message_time_micro = servertime;
                            }
                            __result = __instance.defaultBlock;
                            return false;
                        }
                    }
                }
                if (dump_micro)
                {
                    dump_micro = false;
                    api.Logger.Event("Dump in MicroBlockSounds.get_block at ({0}) BlockIds [{1}] ", __instance.be.Pos, blockids == null ? "" : String.Join(", ", blockids));
                }
                return true;
            }
        }

    }

}

