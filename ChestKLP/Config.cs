using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TShockAPI;

namespace ChestKLP
{

    public class Config
    {
        public CONFIG_MAIN Main;
        public CONFIG_DB DB;

        static string path = Path.Combine(TShock.SavePath, "ChestKLP_Config.json");

        public static Config Read()
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(Default(), Formatting.Indented));
                return Default();
            }

            var args = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));

            if (args == null) return Default();


            if (args.Main == null) { args.Main = new(); } else { args.Main.FixNull(); }
            
            if (args.DB == null) { args.DB = new(); } else { args.DB.FixNull(); }


            File.WriteAllText(path, JsonConvert.SerializeObject(args, Formatting.Indented));
            return args;
        }

        /// <summary>
        /// changes config file
        /// </summary>
        /// <param name="config"></param>
        public void Changeall()
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(Default(), Formatting.Indented));
            }
            else
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
        }

        private static Config Default()
        {
            return new Config()
            {
                Main = new(),
                DB = new(),
            };
        }

        public class CONFIG_MAIN
        {
            public bool? SelfChest_To_Vault = false;

            public string SelfChest_CMD_Permission = "chestklp.selfchest.modify";
            public string UnliChest_CMD_Permission = "chestklp.unlichest.modify";
            public CONFIG_MAIN() { }

            public void FixNull()
            {
                CONFIG_MAIN getdefault = new();

                if (SelfChest_To_Vault == null) SelfChest_To_Vault = getdefault.SelfChest_To_Vault;

                if (SelfChest_CMD_Permission == null) SelfChest_CMD_Permission = getdefault.SelfChest_CMD_Permission;
                if (UnliChest_CMD_Permission == null) UnliChest_CMD_Permission = getdefault.UnliChest_CMD_Permission;
            }
        }
        public class CONFIG_DB
        {
            public string StorageType = "sqlite";
            public string SqliteDBPath = "ChestKLP.sqlite";
            public string MySqlHost = "localhost:3306";
            public string MySqlDbName = "";
            public string MySqlUsername = "";
            public string MySqlPassword = "";
            public CONFIG_DB() { }

            public void FixNull()
            {
                CONFIG_DB getdefault = new();

                if (StorageType == null) StorageType = getdefault.StorageType;
                if (SqliteDBPath == null) SqliteDBPath = getdefault.SqliteDBPath;
                if (MySqlHost == null) MySqlHost = getdefault.MySqlHost;
                if (MySqlDbName == null) MySqlDbName = getdefault.MySqlDbName;
                if (MySqlUsername == null) MySqlUsername = getdefault.MySqlUsername;
                if (MySqlPassword == null) MySqlPassword = getdefault.MySqlPassword;
            }
        }
    }
}
