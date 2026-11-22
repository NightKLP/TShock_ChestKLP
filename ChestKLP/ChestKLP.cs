using Microsoft.Xna.Framework;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using System.Data;
using Terraria;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ChestKLP
{
    [ApiVersion(2, 1)]
    public class ChestKLP : TerrariaPlugin
    {

        #region [ Plugin Info ]
        public override string Author => "Nightklp";
        public override string Description => "lets your chest have a unique function";
        public override string Name => "ChestKLP";
        public override System.Version Version => new System.Version(1, 1);
        #endregion


        public static Config Config = Config.Read(); //CONFIG


        // Main DataBase
        public static IDbConnection MainIDBC = getsqlcon(Config.DB.StorageType.ToLower());
        public static MainDB MainDBManager = new MainDB(MainIDBC);

        public static IDbConnection getsqlcon(string StorageType)
        {
            if (StorageType == "sqlite")
            {
                string sql = Path.Combine(TShock.SavePath, Config.DB.SqliteDBPath);
                Directory.CreateDirectory(Path.GetDirectoryName(sql));
                return new Microsoft.Data.Sqlite.SqliteConnection(string.Format("Data Source={0}", sql));
            }
            else if (StorageType == "mysql")
            {
                try
                {
                    var hostport = Config.DB.MySqlHost.Split(':');
                    MySqlConnection DB = new MySqlConnection();
                    DB.ConnectionString =
                        String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            hostport[0],
                            hostport.Length > 1 ? hostport[1] : "3306",
                            Config.DB.MySqlDbName,
                            Config.DB.MySqlUsername,
                            Config.DB.MySqlPassword
                            );
                    return DB;
                }
                catch (MySqlException ex)
                {
                    throw new Exception("MySql not setup correctly");
                }
            }
            else
            {
                throw new Exception("Invalid storage type");
            }
        }


        public enum ChestModifyType
        {
            AddSelfChest,
            RemoveSelfChest,
            EditSelfChest,

            AddUnliChest,
            RemoveUnliChest,
            EditUnliChest,
        }

        public Dictionary<string, ChestModifyType> PlayerWhoModify = new();

        public ChestKLP(Main game) : base(game)
        {
            //amogus
        }

        #region [ Initialize ]
        public override void Initialize()
        {
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);

            GetDataHandlers.ChestOpen += ChestOpen;
            GetDataHandlers.ChestItemChange += ChestItemChange;
            GetDataHandlers.PlaceChest += HandlePlaceChest;
            OTAPI.Hooks.Chest.QuickStack += OnQuickStack;

            GeneralHooks.ReloadEvent += OnReload;

            Commands.ChatCommands.Add(new Command(Config.Main.SelfChest_CMD_Permission, CMD_SelfChest, "selfchest")
            {
                HelpText = "able to modify the SelfChest"
            });
            Commands.ChatCommands.Add(new Command(Config.Main.UnliChest_CMD_Permission, CMD_UnliChest, "unlichest")
            {
                HelpText = "able to modify the UnliChest"
            });

            MainDBManager.INITChestKLP();
        }
        #endregion

        #region [ Dispose ]
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);

                GetDataHandlers.ChestOpen -= ChestOpen;
                GetDataHandlers.ChestItemChange -= ChestItemChange;
                GetDataHandlers.PlaceChest -= HandlePlaceChest;
                OTAPI.Hooks.Chest.QuickStack -= OnQuickStack;

                GeneralHooks.ReloadEvent -= OnReload;
            }
        }
        #endregion

        #region [ Update Track ]


        DateTime ChestCheck = DateTime.MinValue;
        private void OnUpdate(EventArgs args)
        {
            if ((DateTime.UtcNow - ChestCheck).TotalSeconds <= 3) { return; }
            ChestCheck = DateTime.UtcNow;


            if (AllPlayersIsOpenChest()) { return; }

            if (MainDBManager.QueueChestAdminUpdate.Count > 0)
            {
                OnUpdateChestAdmin = true;
                MainDBManager.UpdateSyncChestKLPData();
            }

            if (MainDBManager.QueueChestUpdate.Count > 0)
            {
                OnUpdateChest = true;
                MainDBManager.UpdateSyncSelfChestData();
            }
        }

        #endregion

        #region [{ Handlers }]

        internal static bool OnUpdateChest = false;
        internal static bool OnUpdateChestAdmin = false;

        private void OnReload(ReloadEventArgs args)
        {
            OnUpdateChest = true;
            OnUpdateChestAdmin = true;
            MainDBManager.UpdateSyncSelfChestData();
            MainDBManager.UpdateSyncChestKLPData();
            Config = Config.Read();

            args.Player.SendInfoMessage("ChestKLP Data & Config Reloaded!");
        }

        private void ChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs args)
        {
            #region code

            Chest getchest = Main.chest[args.ID];

            if (getchest == null)
            {
                return;
            }

            ChestData chestdata;
            if (!MainDBManager.TryGetChestData(getchest, out chestdata))
            {
                return;
            }

            if (PlayerWhoModify.ContainsKey(args.Player.Name))
            {
                ChestModifyType modifytype = PlayerWhoModify[args.Player.Name];

                switch (modifytype)
                {
                    case ChestModifyType.EditSelfChest:
                        {
                            MainDBManager.UpdateChestKLPItemData(getchest, new(args.Slot, new(args.Type, args.Stacks, args.Prefix)));
                            args.Player.SendWarningMessage("SelfChest ItemChange: " + args.ID);
                            return;
                        }
                    case ChestModifyType.EditUnliChest:
                        {
                            MainDBManager.UpdateChestKLPItemData(getchest, new(args.Slot, new(args.Type, args.Stacks, args.Prefix)));
                            args.Player.SendWarningMessage("UnliChest ItemChange: " + args.ID);
                            return;
                        }
                }
            }

            switch (chestdata.Type)
            {
                case ChestKLPType.SelfChest:
                    {
                        MainDBManager.UpdateSelfChestItemData(args.Player, getchest, new(args.Slot, new(args.Type, args.Stacks, args.Prefix)));
                        return;
                    }
                case ChestKLPType.UnliChest:
                default:
                    {
                        args.Handled = true;
                        return;
                    }
            }
            #endregion
        }

        private void HandlePlaceChest(object? sender, GetDataHandlers.PlaceChestEventArgs args)
        {
            #region code

            DebugLogKLP($"PlaceChest : executed A1");

            if (!MainDBManager.TryGetChestData(args.TileX, args.TileY, out _))
            {
                DebugLogKLP($"PlaceChest : executed B1");
                return;
            }

            DebugLogKLP($"PlaceChest : executed A2");
            args.Player.SendTileSquareCentered(args.TileX, args.TileY, 4);
            args.Handled = true;

            #endregion
        }
        private void ChestOpen(object? sender, GetDataHandlers.ChestOpenEventArgs args)
        {
            #region code

            DebugLogKLP($"ChestOpen : executed A1");
            int getchestid = GetChestIDByPos(args.X, args.Y);

            if (getchestid == -1)
            {
                DebugLogKLP($"ChestOpen : pos id not found");
                return;
            }

            DebugLogKLP($"ChestOpen : executed A2");
            Chest getchest = Main.chest[getchestid];

            if (getchest == null)
            {
                DebugLogKLP($"ChestOpen : returned null");
                return;
            }

            if (PlayerWhoModify.ContainsKey(args.Player.Name))
            {
                DebugLogKLP($"ChestOpen : executed B1 ( player modify )");
                if (OnUpdateChestAdmin)
                {
                    DebugLogKLP($"ChestOpen : OnUpdateChestAdmin");
                    args.Player.SendErrorMessage("Cannot Open the chest at the moment!");
                    args.Handled = true;
                    return;
                }

                DebugLogKLP($"ChestOpen : executed B2");
                args.Handled = true;

                DebugLogKLP($"ChestOpen : executed B3");
                ChestModifyType modifytype = PlayerWhoModify[args.Player.Name];

                DebugLogKLP($"ChestOpen : executed B4");
                switch (modifytype)
                {
                    case ChestModifyType.AddUnliChest:
                        #region [ Add UnliChest ]
                        {
                            DebugLogKLP($"ChestOpen : executed B A1");

                            if (MainDBManager.TryGetChestData(args.X, args.Y, out ChestData get))
                            {
                                DebugLogKLP($"ChestOpen : executed B A2 (invalid)");
                                args.Player.SendErrorMessage($"This Chest Data Already Existed!" +
                                    $"\nChestType: {get.Type.ToString()}");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B A3");
                            if (MainDBManager.CreateNewChestKLP(getchest, ChestKLPType.UnliChest))
                            {
                                DebugLogKLP($"ChestOpen : executed B A4");
                                args.Player.SendSuccessMessage($"({args.X}, {args.Y}) UnliChest has been added!");
                            } else
                            {
                                DebugLogKLP($"ChestOpen : executed B A5");
                                args.Player.SendErrorMessage("Unable to add UnliChest!");
                            }
                            return;
                        }
                        #endregion
                    case ChestModifyType.AddSelfChest:
                        #region [ Add SelfChest ]
                        {
                            DebugLogKLP($"ChestOpen : executed B B1");
                            if (MainDBManager.TryGetChestData(args.X, args.Y, out ChestData get))
                            {
                                DebugLogKLP($"ChestOpen : executed B B2 (invalid)");
                                args.Player.SendErrorMessage($"This Chest Data Already Existed!" +
                                    $"\nChestType: {get.Type.ToString()}");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B B3");
                            if (MainDBManager.CreateNewChestKLP(getchest, ChestKLPType.SelfChest))
                            {
                                DebugLogKLP($"ChestOpen : executed B B4");
                                args.Player.SendSuccessMessage($"({args.X}, {args.Y}) SelfChest has been added!");
                            } else
                            {
                                DebugLogKLP($"ChestOpen : executed B B5");
                                args.Player.SendErrorMessage("Unable to add SelfChest!");
                            }
                            return;
                        }
                        #endregion
                    case ChestModifyType.RemoveUnliChest:
                        #region [ Remove UnliChest ]
                        {
                            DebugLogKLP($"ChestOpen : executed B C1");
                            if (!MainDBManager.TryGetChestData(args.X, args.Y, out ChestData get))
                            {
                                DebugLogKLP($"ChestOpen : executed B C2 (invalid)");
                                args.Player.SendErrorMessage($"Cannot Find Chest Data of ({args.X}, {args.Y})!");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B C3");
                            if (get.Type != ChestKLPType.UnliChest)
                            {
                                DebugLogKLP($"ChestOpen : executed B C4 (invalidtype)");
                                args.Player.SendErrorMessage($"This chest isn't UnliChest!" +
                                    $"\nChestType: {get.Type.ToString()}");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B C5");
                            if (MainDBManager.RemoveChestKLP(getchest))
                            {
                                DebugLogKLP($"ChestOpen : executed B C6");
                                args.Player.SendSuccessMessage($"({args.X}, {args.Y}) UnliChest has been Removed!");
                            } else
                            {
                                DebugLogKLP($"ChestOpen : executed B C7");
                                args.Player.SendErrorMessage("Unable to remove UnliChest!");
                            }
                            return;
                        }
                        #endregion
                    case ChestModifyType.RemoveSelfChest:
                        #region [ Remove SelfChest ]
                        {
                            DebugLogKLP($"ChestOpen : executed B D1");
                            if (!MainDBManager.TryGetChestData(args.X, args.Y, out ChestData get))
                            {
                                DebugLogKLP($"ChestOpen : executed B D2 (invalid)");
                                args.Player.SendErrorMessage($"Cannot Find Chest Data of ({args.X}, {args.Y})!");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B D3");
                            if (get.Type != ChestKLPType.SelfChest)
                            {
                                DebugLogKLP($"ChestOpen : executed B D4 (invalidtype)");
                                args.Player.SendErrorMessage($"This chest isn't SelfChest!" +
                                    $"\nChestType: {get.Type.ToString()}");
                                return;
                            }

                            DebugLogKLP($"ChestOpen : executed B D5");
                            if (MainDBManager.RemoveChestKLP(getchest))
                            {
                                DebugLogKLP($"ChestOpen : executed B D6");
                                args.Player.SendSuccessMessage($"({args.X}, {args.Y}) SelfChest has been Removed!");
                            } else
                            {
                                DebugLogKLP($"ChestOpen : executed B D7");
                                args.Player.SendErrorMessage("Unable to remove SelfChest!");
                            }
                            return;
                        }
                    #endregion
                    case ChestModifyType.EditSelfChest:
                    case ChestModifyType.EditUnliChest:
                        {
                            DebugLogKLP($"ChestOpen : executed B E1");
                            if (!MainDBManager.TryGetChestData(args.X, args.Y, out ChestData get))
                            {
                                DebugLogKLP($"ChestOpen : executed B E2 (invalid)");
                                args.Player.SendErrorMessage($"Cannot Find Chest Data of ({args.X}, {args.Y})!");
                                return;
                            }
                            DebugLogKLP($"ChestOpen : executed B E3");
                            UpdateChestItems(getchestid, get.items);
                            return;
                        }
                    default:
                        {
                            DebugLogKLP($"ChestOpen : executed B F1");
                            args.Player.SendErrorMessage("Unable to get Modify Data Type!");
                            return;
                        }
                }
            }

            DebugLogKLP($"ChestOpen : executed A3");
            if (!MainDBManager.TryGetChestData(args.X, args.Y, out ChestData chestdata))
            {
                DebugLogKLP($"ChestOpen : executed A4");
                return;
            }

            DebugLogKLP($"ChestOpen : executed A5");
            if (OnUpdateChest)
            {
                DebugLogKLP($"ChestOpen : executed A6");
                args.Player.SendErrorMessage("Cannot Open the chest at the moment!");
                args.Handled = true;
                return;
            }

            DebugLogKLP($"ChestOpen : executed A7");
            switch (chestdata.Type)
            {
                case ChestKLPType.SelfChest:
                    #region ( Self Chest )
                    {
                        DebugLogKLP($"ChestOpen : executed A A1");
                        if ((bool)Config.Main.SelfChest_To_Vault)
                        {
                            DebugLogKLP($"ChestOpen : executed A A A1");
                            PlrSelfChest getplrdata;
                            if (!chestdata.TryGetPlrSelfChest(args.Player.Name, out getplrdata))
                            {
                                DebugLogKLP($"ChestOpen : executed A A A2");
                                UpdateChestItems(getchestid, chestdata.items);
                            }
                            else
                            {
                                DebugLogKLP($"ChestOpen : executed A A A3");
                                UpdateChestItems(getchestid, getplrdata.items);
                            }
                            return;
                        }
                        else
                        {
                            DebugLogKLP($"ChestOpen : executed A A B1");
                            args.Handled = true;

                            DebugLogKLP($"ChestOpen : executed A A B2");
                            bool IsEmpty = true;

                            DebugLogKLP($"ChestOpen : executed A A B3");
                            PlrSelfChest getplrdata;
                            if (!chestdata.TryGetPlrSelfChest(args.Player.Name, out getplrdata))
                            {
                                DebugLogKLP($"ChestOpen : executed A A B4");
                                NetItem[] getitems = (NetItem[])chestdata.items.Clone();

                                DebugLogKLP($"ChestOpen : executed A A B5");
                                for (int i = 0; i < getitems.Length; i++)
                                {
                                    if (getitems[i].NetId == 0) continue;
                                    IsEmpty = false;

                                    if (!args.Player.InventorySlotAvailable)
                                    {
                                        args.Player.SendErrorMessage("Your Inventory Is Full!" +
                                            "\nThere's still remaining items in chest...");
                                        break;
                                    }

                                    args.Player.GiveItem(getitems[i].NetId, getitems[i].Stack, getitems[i].PrefixId);

                                    getitems[i] = NetItemEmpty();
                                }
                                DebugLogKLP($"ChestOpen : executed A A B6");

                                chestdata.AddNewPlrSelfChest(new PlrSelfChest(args.Player.Name, getitems));

                                DebugLogKLP($"ChestOpen : executed A A B7");
                                if (!MainDBManager.QueueChestUpdate.ContainsKey(chestdata.Pos))
                                {
                                    DebugLogKLP($"ChestOpen : executed A A B8");
                                    MainDBManager.QueueChestUpdate.Add(chestdata.Pos, new() { { args.Player.Name, true } });
                                }
                                else
                                {
                                    DebugLogKLP($"ChestOpen : executed A A B9");
                                    if (!MainDBManager.QueueChestUpdate[chestdata.Pos].ContainsKey(args.Player.Name))
                                    {
                                        DebugLogKLP($"ChestOpen : executed A A B10");
                                        MainDBManager.QueueChestUpdate[chestdata.Pos].Add(args.Player.Name, true);
                                    }
                                }
                            }
                            else
                            {
                                DebugLogKLP($"ChestOpen : executed A A C1");
                                for (int i = 0; i < getplrdata.items.Length; i++)
                                {
                                    if (getplrdata.items[i].NetId == 0) continue;
                                    IsEmpty = false;

                                    if (!args.Player.InventorySlotAvailable)
                                    {
                                        args.Player.SendErrorMessage("Your Inventory Is Full!" +
                                            "\nThere's still remaining items in chest...");
                                        break;
                                    }

                                    args.Player.GiveItem(getplrdata.items[i].NetId, getplrdata.items[i].Stack, getplrdata.items[i].PrefixId);

                                    getplrdata.items[i] = NetItemEmpty();
                                }
                                DebugLogKLP($"ChestOpen : executed A A C2");

                                if (!MainDBManager.QueueChestUpdate.ContainsKey(chestdata.Pos))
                                {
                                    DebugLogKLP($"ChestOpen : executed A A C3");
                                    MainDBManager.QueueChestUpdate.Add(chestdata.Pos, new() { { args.Player.Name, false } });
                                }
                                else
                                {
                                    DebugLogKLP($"ChestOpen : executed A A C4");
                                    if (!MainDBManager.QueueChestUpdate[chestdata.Pos].ContainsKey(args.Player.Name))
                                    {
                                        DebugLogKLP($"ChestOpen : executed A A C5");
                                        MainDBManager.QueueChestUpdate[chestdata.Pos].Add(args.Player.Name, false);
                                    }
                                }
                            }
                            DebugLogKLP($"ChestOpen : executed A A C6");

                            args.Player.SendSuccessMessage(IsEmpty ? "you already took everything on the chest!" : "you took items from the chest.");
                        }
                        return;
                    }
                    #endregion
            }

            #endregion
        }


        private static void OnQuickStack(object? sender, OTAPI.Hooks.Chest.QuickStackEventArgs args)
        {
            #region code

            Chest getchest = Main.chest[args.ChestIndex];

            if (getchest == null)
            {
                return;
            }

            if (!MainDBManager.TryGetChestData(getchest, out ChestData chestdata))
            {
                return;
            }

            if (chestdata.Type is
                ChestKLPType.UnliChest or
                ChestKLPType.SelfChest)
            {
                args.Result = OTAPI.HookResult.Cancel;
                return;
            }
            #endregion
        }

        #endregion

        #region =[ Commands ]=


        private void CMD_SelfChest(CommandArgs args)
        {
            #region code

            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("Proper Usage: /selfchest <sub-command>\n");
                args.Player.SendInfoMessage("==== Sub-Command ====" +
                    "\n'/selfchest change' : able to change items on selfchest" +
                    "\n'/selfchest add' : able to add selfchest" +
                    "\n'/selfchest remove' : able to remove selfchest\n\n" +
                    TShock.Utils.ColorTag("'/selfchest setup' : ChangeAll chests in this world into SelfChest", Color.OrangeRed));
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "changeitem":
                case "change":
                case "item":
                case "edit":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.EditSelfChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer change selfchest items");
                                PlayerWhoModify.Remove(args.Player.Name);
                            } else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now change selfchest items");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.EditSelfChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now change selfchest items");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.EditSelfChest);
                        }
                        return;
                    }
                case "placechest":
                case "addchest":
                case "add":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.AddSelfChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer add selfchest");
                                PlayerWhoModify.Remove(args.Player.Name);
                            }
                            else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now add selfchest");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.AddSelfChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now add selfchest");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.AddSelfChest);
                        }
                        return;
                    }
                case "removechest":
                case "remove":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.RemoveSelfChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer remove selfchest");
                                PlayerWhoModify.Remove(args.Player.Name);
                            }
                            else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now remove selfchest");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.RemoveSelfChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now remove selfchest");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.RemoveSelfChest);
                        }
                        return;
                    }
                case "setup":
                    {
                        OnUpdateChest = true;
                        OnUpdateChestAdmin = true;
                        args.Player.SendMessage($"Setting up SelfChest...", Color.LightYellow);

                        if (MainDBManager.RemoveAllChestKLP())
                        {
                            args.Player.SendMessage($"All ChestKLP Data on This World is deleted...", Color.Olive);
                        }

                        if (MainDBManager.CreateNewChestKLP(ChestKLPType.SelfChest, Main.chest))
                        {
                            args.Player.SendMessage($"All ChestKLP Data on This World converted to SelfChest...", Color.Olive);
                        }

                        args.Player.SendSuccessMessage("SelfChest has been setup!");
                        return;
                    }
                default:
                    {
                        args.Player.SendErrorMessage("Invalid Sub-Commands!\n");
                        args.Player.SendInfoMessage("==== Sub-Commands ====" +
                            "\n'/selfchest change' : able to change items on selfchest" +
                            "\n'/selfchest add' : able to add selfchest" +
                            "\n'/selfchest remove' : able to remove selfchest");
                        return;
                    }
            }
            #endregion
        }
        
        private void CMD_UnliChest(CommandArgs args)
        {
            #region code

            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("Proper Usage: /unlichest <sub-command>\n");
                args.Player.SendInfoMessage("==== Sub-Commands ====" +
                    "\n'/unlichest change' : able to change items on unlichest" +
                    "\n'/unlichest add' : able to add unlichest" +
                    "\n'/unlichest remove' : able to remove unlichest");
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "changeitem":
                case "change":
                case "item":
                case "edit":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.EditUnliChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer change unlichest items");
                                PlayerWhoModify.Remove(args.Player.Name);
                            } else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now change unlichest items");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.EditUnliChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now change unlichest items");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.EditUnliChest);
                        }
                        return;
                    }
                case "placechest":
                case "addchest":
                case "add":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.AddUnliChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer add unlichest");
                                PlayerWhoModify.Remove(args.Player.Name);
                            }
                            else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now add unlichest");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.AddUnliChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now add unlichest");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.AddUnliChest);
                        }
                        return;
                    }
                case "removechest":
                case "remove":
                    {
                        if (PlayerWhoModify.ContainsKey(args.Player.Name))
                        {
                            if (PlayerWhoModify[args.Player.Name] == ChestModifyType.RemoveUnliChest)
                            {
                                args.Player.SendSuccessMessage("you can no longer remove unlichest");
                                PlayerWhoModify.Remove(args.Player.Name);
                            }
                            else
                            {
                                args.Player.SendSuccessMessage("(Override ModifyType) you can now remove unlichest");
                                PlayerWhoModify.Add(args.Player.Name, ChestModifyType.RemoveUnliChest);
                            }
                        }
                        else
                        {
                            args.Player.SendSuccessMessage("you can now remove unlichest");
                            PlayerWhoModify.Add(args.Player.Name, ChestModifyType.RemoveUnliChest);
                        }
                        return;
                    }
                default:
                    {
                        args.Player.SendErrorMessage("Invalid Sub-Commands!\n");
                        args.Player.SendInfoMessage("==== Sub-Commands ====" +
                            "\n'/unlichest change' : able to change items on unlichest" +
                            "\n'/unlichest add' : able to add unlichest" +
                            "\n'/unlichest remove' : able to remove unlichest");
                        return;
                    }
            }
            #endregion
        }

        #endregion


        public static void DebugLogKLP(string text)
        {
            if (!(bool)Config.Main.UseDebugLog) { return; }

            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write("[Debug-ChestKLP] ");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public void UpdateChestItems(int chestindex, NetItem[] gitems)
        {
            for (int i = 0; i < gitems.Length && i < Main.chest[chestindex].item.Length; i++)
            {
                Main.chest[chestindex].item[i] = gitems[i].ToItem();
                TSPlayer.All.SendData(PacketTypes.ChestItem, "", chestindex, i);
            }
        }

        public static int GetChestID(Chest chest)
        {
            return GetChestIDByPos(chest.x, chest.y);
        }

        public static int GetChestIDByPos(Point16 pos)
        {
            return GetChestIDByPos(pos.X, pos.Y);
        }
        public static int GetChestIDByPos(Vector2 pos)
        {
            return GetChestIDByPos((int)pos.X, (int)pos.Y);
        }
        public static int GetChestIDByPos(int x, int y)
        {
            try
            {
                for (int i = 0; i < Main.chest.Length; i++)
                {
                    if (Main.chest[i] == null) { continue; }
                    if (Main.chest[i].x == x && Main.chest[i].y == y)
                    {
                        return i;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogKLP($"GetChestIDByPos : Error - {ex.Message}");
            }
            return -1;
        }

        public static NetItem NetItemEmpty()
        {
            return new(0, 0, 0);
        }

        public static bool AllPlayersIsOpenChest()
        {
            foreach (TSPlayer player in TShock.Players)
            {
                if (player == null) { continue; }
                if (!player.RealPlayer) { continue; }
                if (player.ActiveChest >= 1) { return true; }
            }
            return false;
        }
    }
}
