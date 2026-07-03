using Microsoft.Xna.Framework;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.DataStructures;
using TShockAPI;
using TShockAPI.DB;

namespace ChestKLP
{
    public class MainDB
    {
        public IDbConnection _db;

        public MainDB(IDbConnection db)
        {
            _db = db;

            var sqlCreator = new SqlTableCreator(db, db.GetSqlQueryBuilder());

            sqlCreator.EnsureTableStructure(new SqlTable(ChestData.TableName_Main,
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                new SqlColumn("XY", MySqlDbType.Text),
                new SqlColumn("WorldID", MySqlDbType.Text),
                new SqlColumn("ChestType", MySqlDbType.Int32),
                new SqlColumn("Items", MySqlDbType.Text)));


            sqlCreator.EnsureTableStructure(new SqlTable(ChestData.TableName_SelfChestData,
                new SqlColumn("ChestKLPID", MySqlDbType.Int32),
                new SqlColumn("PlayerName", MySqlDbType.VarChar, 50),
                new SqlColumn("Items", MySqlDbType.Text)));


        }

        /// <exception cref="NullReferenceException"></exception>

        #region [ QUERYREADER ]

        public IDbConnection getDB()
        {
            return _db;
        }

        #endregion


        #region [ ChestKLP ]

        public ChestData[] getchestklp = { };


        public void INITChestKLP()
        {
            ChestKLP.OnUpdateChestAdmin = true;
            ChestKLP.OnUpdateChest = true;
            SyncChestKLP();
        }

        public void SyncChestKLP()
        {
            ChestKLP.OnUpdateChestAdmin = true;
            ChestKLP.OnUpdateChest = true;

            List<ChestData> result = new List<ChestData>();
            using var reader = _db.QueryReader($"SELECT * FROM {ChestData.TableName_Main} WHERE WorldID = @0", Main.worldID.ToString());

            while (reader.Read())
            {
                int getID = reader.Get<int>("ID");

                Point16 pos = Point16.Zero;
                if (reader.Get<string>("XY") != null)
                {
                    string[] xy = reader.Get<string>("XY").Split(',');
                    pos = new Point16(int.Parse(xy[0]), int.Parse(xy[1]));
                }
                ChestKLPType type = (ChestKLPType)reader.Get<int>("ChestType");
                NetItem[] items = StringToNetItemArray(reader.Get<string>("Items"));


                using var reader2 = _db.QueryReader($"SELECT * FROM {ChestData.TableName_SelfChestData} WHERE ChestKLPID = @0", getID);

                List<PlrSelfChest> selfchestdataplr = new();
                while (reader2.Read())
                {
                    selfchestdataplr.Add(new(
                        reader2.Get<string>("PlayerName"),
                        StringToNetItemArray(reader2.Get<string>("Items"))
                        ));
                }
                result.Add(new ChestData(getID, pos, type, items, selfchestdataplr.ToArray()));
            }


            getchestklp = result.ToArray();
            ChestKLP.OnUpdateChestAdmin = false;
            ChestKLP.OnUpdateChest = false;
        }

        #region [ Check ]
        public bool ChestKLPExist(Chest chest)
        {
            return ChestKLPExist(chest.x, chest.y);
        }

        public bool ChestKLPExist(Point16 pos)
        {
            return ChestKLPExist(pos.X, pos.Y);
        }
        public bool ChestKLPExist(Vector2 pos)
        {
            return ChestKLPExist((int)pos.X, (int)pos.Y);
        }
        public bool ChestKLPExist(int X, int Y)

        {
            return getchestklp.Any(c => c.Pos.X == X && c.Pos.Y == Y);
        }

        #endregion

        #region [ Create/Delete ]

        public bool CreateNewChestKLP(Chest chest, ChestKLPType chesttype)
        {
            if (ChestKLPExist(chest)) { return true; }

            string query = $"INSERT INTO {ChestData.TableName_Main} (" +
                "ID, " + // 0
                "XY, " + // 1
                "WorldID, " + // 2
                "ChestType, " + // 3
                "Items) " + // 4
                "VALUES (@0, @1, @2, @3, @4);" + _db.GetSqlType() switch
            {
                SqlType.Mysql => "; SELECT LAST_INSERT_ID();",
                SqlType.Sqlite => "; SELECT last_insert_rowid();",
                SqlType.Postgres => "RETURNING \"Identifier\";",
                _ => null,
            };
            int num = _db.QueryScalar<int>(query,
                null, //ID
                $"{chest.x},{chest.y}", //XY
                Main.worldID.ToString(), //WorldID
                (int)chesttype, //ChestType
                NetItemArrayToString(GetNetItemsFromChest(chest)) //Items
                );

            if (num != 0)
            {
                List<ChestData> result = getchestklp.ToList();
                result.Add(new ChestData(
                    num,
                    new Point16(chest.x, chest.y),
                    chesttype,
                    GetNetItemsFromChest(chest),
                    new PlrSelfChest[] { }
                    ));
                getchestklp = result.ToArray();
                return true;
            }

            return false;
        }

        public bool CreateNewChestKLP(ChestKLPType type, params Chest[] chests)
        {
            ChestKLP.OnUpdateChestAdmin = true;
            ChestKLP.OnUpdateChest = true;
            if (chests == null || chests.Length == 0)
            {
                ChestKLP.OnUpdateChestAdmin = false;
                ChestKLP.OnUpdateChest = false;
                return false;
            }

            var sb = new StringBuilder();
            var args = new List<object>();

            sb.Append($"INSERT INTO {ChestData.TableName_Main} ");
            sb.Append("(XY, WorldID, ChestType, Items) VALUES ");

            int count = 0;

            for (int i = 0; i < chests.Length; i++)
            {
                if (chests[i] == null) { continue; }
                // Skip if already exists
                if (ChestKLPExist(chests[i])) { continue; }

                if (count > 0)
                    sb.Append(",");

                int p = count * 4;

                sb.Append($"(@{p}, @{p + 1}, @{p + 2}, @{p + 3})");

                args.Add($"{chests[i].x},{chests[i].y}");
                args.Add(Main.worldID.ToString());
                args.Add((int)type);
                args.Add(NetItemArrayToString(GetNetItemsFromChest(chests[i])));

                count++;
            }

            if (count == 0)
            {
                ChestKLP.OnUpdateChestAdmin = false;
                ChestKLP.OnUpdateChest = false;
                return false;
            }

            sb.Append(";");

            _db.Query(sb.ToString(), args.ToArray());

            SyncChestKLP();
            return true;
        }

        public bool RemoveChestKLP(Chest chest)
        {
            if (!ChestKLPExist(chest)) { return true; }
            bool isremoved = _db.Query($"DELETE FROM {ChestData.TableName_Main} WHERE XY = @0 AND WorldID = @1",
                $"{chest.x},{chest.y}",
                Main.worldID.ToString()) != 0;

            List<ChestData> result = getchestklp.ToList();
            if (isremoved)
            {
                result.RemoveAll(c => c.Pos.X == chest.x && c.Pos.Y == chest.y);
            }
            getchestklp = result.ToArray();
            return isremoved;
        }

        public bool RemoveAllChestKLP()
        {
            ChestKLP.OnUpdateChestAdmin = true;
            ChestKLP.OnUpdateChest = true;

            bool isremoved = _db.Query($"DELETE FROM {ChestData.TableName_Main} WHERE WorldID = @0",
                Main.worldID.ToString()) != 0;

            getchestklp = new ChestData[] {};

            ChestKLP.OnUpdateChestAdmin = false;
            ChestKLP.OnUpdateChest = false;
            return isremoved;
        }

        public bool WipeAllDataChestKLP()
        {
            ChestKLP.OnUpdateChestAdmin = true;
            ChestKLP.OnUpdateChest = true;

            bool isremoved = _db.Query($"DELETE FROM {ChestData.TableName_Main}",
                Main.worldID.ToString()) != 0;

            getchestklp = new ChestData[] {};

            ChestKLP.OnUpdateChestAdmin = false;
            ChestKLP.OnUpdateChest = false;
            return isremoved;
        }

        #endregion

        #region [ Get ]
        public bool TryGetChestData(Chest chest, out ChestData chestdata)
        {
            return TryGetChestData(chest.x, chest.y, out chestdata);
        }
        public bool TryGetChestData(Point16 pos, out ChestData chestdata)
        {
            return TryGetChestData(pos.X, pos.Y, out chestdata);
        }
        public bool TryGetChestData(Vector2 pos, out ChestData chestdata)
        {
            return TryGetChestData((int)pos.X, (int)pos.Y, out chestdata);
        }
        public bool TryGetChestData(int X, int Y, out ChestData chestdata)
        {
            for (int i = 0; i < getchestklp.Length; i++)
            {
                if (getchestklp[i].Pos.X == X && getchestklp[i].Pos.Y == Y)
                {
                    chestdata = getchestklp[i];
                    return true;
                }
            }
            chestdata = null;
            return false;
        }
        #endregion


        public List<Point16> QueueChestAdminUpdate = new();
        public void UpdateChestKLPItemData(Chest chest, (int, NetItem) CurrentItemChange)
        {
            if (TryGetChestData(chest, out ChestData chestdata))
            {
                chestdata.items = GetNetItemsFromChest(chest, CurrentItemChange);

                if (!QueueChestAdminUpdate.Contains(chestdata.Pos))
                {
                    QueueChestAdminUpdate.Add(chestdata.Pos);
                }
            }
        }

        //chestpos | (players that interact, is new data )
        public Dictionary<Point16, Dictionary<string, bool>> QueueChestUpdate = new();
        public void UpdateSelfChestItemData(TSPlayer player, Chest chest, (int, NetItem) CurrentItemChange)
        {
            if (ChestKLP.GetChestID(chest) != player.ActiveChest) { return; }
            for (int i = 0; i < getchestklp.Length; i++)
            {
                if (getchestklp[i].Pos.X == chest.x && getchestklp[i].Pos.Y == chest.y)
                {
                    for (int ii = 0; ii < getchestklp[i].PlayersUsed.Length; ii++)
                    {
                        if (getchestklp[i].PlayersUsed[ii].PlayerName == player.Name)
                        {
                            getchestklp[i].PlayersUsed[ii].items = GetNetItemsFromChest(chest, CurrentItemChange);

                            if (!QueueChestUpdate.ContainsKey(getchestklp[i].Pos))
                            {
                                QueueChestUpdate.Add(getchestklp[i].Pos, new() { { player.Name, false } });
                            }
                            else
                            {
                                if (!QueueChestUpdate[getchestklp[i].Pos].ContainsKey(player.Name))
                                {
                                    QueueChestUpdate[getchestklp[i].Pos].Add(player.Name, false);
                                }
                            }
                            return;
                        }
                    }
                    List<PlrSelfChest> result = getchestklp[i].PlayersUsed.ToList();

                    result.Add(new(player.Name, GetNetItemsFromChest(chest, CurrentItemChange)));

                    getchestklp[i].PlayersUsed = result.ToArray();

                    if (!QueueChestUpdate.ContainsKey(getchestklp[i].Pos))
                    {
                        QueueChestUpdate.Add(getchestklp[i].Pos, new() { { player.Name, true } });
                    }
                    else
                    {
                        if (!QueueChestUpdate[getchestklp[i].Pos].ContainsKey(player.Name))
                        {
                            QueueChestUpdate[getchestklp[i].Pos].Add(player.Name, true);
                        }
                    }
                }
            }

        }

        public void UpdateSyncChestKLPData()
        {

            ChestKLP.OnUpdateChestAdmin = true;
            try
            {
                foreach (var get in QueueChestAdminUpdate)
                {
                    if (TryGetChestData(get, out ChestData chestdata))
                    {
                        _db.Query($"UPDATE {ChestData.TableName_Main} SET Items = @0 WHERE ID = @1",
                            NetItemArrayToString(chestdata.items), chestdata.ID);
                    }
                }
                ChestKLP.OnUpdateChestAdmin = false;
            }
            catch (Exception e)
            {
                ChestKLP.OnUpdateChestAdmin = false;
                Console.WriteLine(e);
            }
            QueueChestAdminUpdate.Clear();
        }

        public void UpdateSyncSelfChestData()
        {
            ChestKLP.OnUpdateChest = true;
            try
            {
                foreach (var get in QueueChestUpdate)
                {
                    if (TryGetChestData(get.Key, out ChestData chestdata))
                    {
                        foreach (var getplr in get.Value)
                        {
                            NetItem[] getcurrentitem = getItemsFromPlayerName(chestdata, getplr.Key);

                            if (getplr.Value)
                            {
                                try
                                {
                                    if (_db.Query($"INSERT INTO {ChestData.TableName_SelfChestData} (" +
                                        "ChestKLPID, " + // 0
                                        "PlayerName, " + // 1
                                        "Items) " + // 2
                                        "VALUES (@0, @1, @2);",
                                        chestdata.ID, //ID
                                        getplr.Key, //Name
                                        NetItemArrayToString(getcurrentitem) //FishingLureLevel
                                        ) == 0)
                                    {
                                        _db.Query($"UPDATE {ChestData.TableName_SelfChestData} SET Items = @0 WHERE PlayerName = @1 AND ChestKLPID = @2", NetItemArrayToString(getcurrentitem), getplr.Key, chestdata.ID);
                                    }

                                }
                                catch
                                {
                                    try
                                    {
                                        _db.Query($"UPDATE {ChestData.TableName_SelfChestData} SET Items = @0 WHERE PlayerName = @1 AND ChestKLPID = @2", NetItemArrayToString(getcurrentitem), getplr.Key, chestdata.ID);
                                    }
                                    catch { }
                                }
                            }
                            else
                            {
                                _db.Query($"UPDATE {ChestData.TableName_SelfChestData} SET Items = @0 WHERE PlayerName = @1 AND ChestKLPID = @2", NetItemArrayToString(getcurrentitem), getplr.Key, chestdata.ID);
                            }

                        }
                    }
                }
                ChestKLP.OnUpdateChest = false;
            } catch (Exception e)
            {
                ChestKLP.OnUpdateChest = false;
                Console.WriteLine(e);
            }

            QueueChestUpdate.Clear();

            return;

            NetItem[] getItemsFromPlayerName(ChestData chestdata, string name)
            {
                for (int i = 0; i < chestdata.PlayersUsed.Length; i++)
                {
                    if (chestdata.PlayersUsed[i].PlayerName == name)
                    {
                        return chestdata.PlayersUsed[i].items;
                    }
                }

                return chestdata.items;
            }
        }



        //misc tools
        public NetItem[] GetNetItemsFromChest(Chest chest, (int, NetItem) CurrentItemChange)
        {
            NetItem[] result = new NetItem[40];

            for (int i = 0; i < result.Length && i < chest.item.Length; i++)
            {
                result[i] = new NetItem(chest.item[i]);
            }

            try { result[CurrentItemChange.Item1] = CurrentItemChange.Item2; } catch { }

            return result;
        }
        public NetItem[] GetNetItemsFromChest(Chest chest)
        {
            NetItem[] result = new NetItem[40];

            for (int i = 0; i < result.Length && i < chest.item.Length; i++)
            {
                result[i] = new NetItem(chest.item[i]);
            }

            return result;
        }


        private NetItem[] StringToNetItemArray(string itemSTR)
        {
            NetItem[] result = new NetItem[40];
            if (string.IsNullOrEmpty(itemSTR)) { return result; }

            if (itemSTR.Contains("|"))
            {
                string[] gitems = itemSTR.Split('|');
                for (int i = 0; i < gitems.Length; i++)
                {
                    string[] itemParts = gitems[i].Split(',');
                    int ItemID = int.Parse(itemParts[0]);
                    int Stack = int.Parse(itemParts[1]);
                    int PrefixID = int.Parse(itemParts[2]);
                    result[i] = new NetItem(ItemID, Stack, (byte)PrefixID);
                }
            } else
            {
                string[] itemParts = itemSTR.Split(',');
                int ItemID = int.Parse(itemParts[0]);
                int Stack = int.Parse(itemParts[1]);
                int PrefixID = int.Parse(itemParts[2]);
                result[0] = new NetItem(ItemID, Stack, (byte)PrefixID);
            }
            return result;
        }

        private string NetItemArrayToString(NetItem[] items)
        {
            //ensure 40index limit
            if (items.Length > 40)
            {
                Array.Resize(ref items, 40);
            }

            return string.Join("|", items.Select(itm => $"{itm.NetId},{itm.Stack},{itm.PrefixId}"));
        }
        #endregion
    }

    public enum ChestKLPType
    {
        UnliChest = 0,
        SelfChest = 1
    }

    public class ChestData
    {
        public const string TableName_Main = "ChestKLP";
        public const string TableName_SelfChestData = "SelfChestPLR";

        public int ID;

        public Point16 Pos;

        public ChestKLPType Type;

        public NetItem[] items;
        public PlrSelfChest[] PlayersUsed;

        public ChestData(int ID, Point16 Pos, ChestKLPType Type, NetItem[] items, PlrSelfChest[] playersUsed)
        {
            this.ID = ID;
            this.Pos = Pos;
            this.Type = Type;
            this.items = items;
            PlayersUsed = playersUsed;
        }

        public void AddNewPlrSelfChest(PlrSelfChest selfplrdata)
        {
            List<PlrSelfChest> result = PlayersUsed.ToList();

            result.Add(selfplrdata);

            PlayersUsed = result.ToArray();
        }
        public bool ChangePlayerName_SelfChest(string PlayerName, string NewPlayerName)
        {
            for (int i = 0; i < PlayersUsed.Length; i++)
            {
                if (PlayersUsed[i].PlayerName == PlayerName)
                {
                    PlayersUsed[i].PlayerName = NewPlayerName;
                    return true;
                }
            }
            return false;
        }
        public bool TryGetPlrSelfChest(string PlayerName, out PlrSelfChest selfplrdata)
        {
            for (int i = 0; i < PlayersUsed.Length; i++)
            {
                if (PlayersUsed[i].PlayerName == PlayerName)
                {
                    selfplrdata = PlayersUsed[i];
                    return true;
                }
            }

            selfplrdata = null;
            return false;
        }
    }

    public class PlrSelfChest
    {
        public string PlayerName;
        public NetItem[] items;

        public PlrSelfChest(string playerName, NetItem[] items)
        {
            PlayerName = playerName;
            this.items = items;
        }
    }
}
