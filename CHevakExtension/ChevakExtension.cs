
using Microsoft.MetadirectoryServices;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Mms_Metaverse
{
    /// <summary>
    /// Summary description for MVExtensionObject.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {

        string ADMA = "chevak.cz";

        // P?ipojovacù ?et?zec se na?ùtù z ChevakExtension.config (vedle DLL v Extensions sloùce)
        private static string _connectionString;
        private static readonly object _configLock = new object();

        private static string IDM_CONNECTION_STRING
        {
            get
            {
                if (_connectionString != null) return _connectionString;
                lock (_configLock)
                {
                    if (_connectionString != null) return _connectionString;
                    _connectionString = LoadConnectionString();
                }
                return _connectionString;
            }
        }

        private static string LoadConnectionString()
        {
            // Utils.ExtensionsDirectory vrùtù cestu k Extensions sloùce MIM Sync Service
            string configPath = Path.Combine(
                Utils.ExtensionsDirectory, "ChevakExtension.config");

            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    "Konfigura?nù soubor nenalezen: " + configPath);

            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = configPath;

            Configuration config = ConfigurationManager.OpenMappedExeConfiguration(
                configMap, ConfigurationUserLevel.None);

            ConnectionStringSettings cs =
                config.ConnectionStrings.ConnectionStrings["IDMDatabase"];

            if (cs == null)
                throw new InvalidOperationException(
                    "Connection string 'IDMDatabase' nebyl nalezen v " + configPath);

            return cs.ConnectionString;
        }

        public MVExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }

        // Sdùlenù SQL p?ipojenù ù otev?eno v Initialize, zav?eno v Terminate
        // Pouùitù: GetGISPermissions si vyùùdù p?ipojenù p?es EnsureConnection()
        private static SqlConnection _sqlConnection;
        private static readonly object _sqlLock = new object();

        void IMVSynchronization.Initialize()
        {
            OpenSqlConnection();
        }

        void IMVSynchronization.Terminate()
        {
            CloseSqlConnection();
        }

        /// <summary>
        /// Otev?e sdÌlenÈ SQL p?ipojenÌ. Vol· se z Initialize() jak MVExtension, tak MAExtension.
        /// </summary>
        public static void OpenSqlConnection()
        {
            lock (_sqlLock)
            {
                if (_sqlConnection == null)
                    _sqlConnection = new SqlConnection(IDM_CONNECTION_STRING);

                if (_sqlConnection.State != System.Data.ConnectionState.Open)
                    _sqlConnection.Open();
            }
        }

        /// <summary>
        /// Zav?e a uvolnÌ sdÌlenÈ SQL p?ipojenÌ. Vol· se z Terminate() jak MVExtension, tak MAExtension.
        /// </summary>
        public static void CloseSqlConnection()
        {
            lock (_sqlLock)
            {
                if (_sqlConnection != null)
                {
                    try { _sqlConnection.Close(); }
                    catch { }
                    _sqlConnection.Dispose();
                    _sqlConnection = null;
                }
            }
        }

        /// <summary>
        /// Vrùtù platnù otev?enù SQL p?ipojenù.
        /// Pokud spojenù vypadlo (timeout, reset sùt?), automaticky se znovu p?ipojù.
        /// </summary>
        private static SqlConnection EnsureConnection()
        {
            lock (_sqlLock)
            {
                if (_sqlConnection == null)
                    _sqlConnection = new SqlConnection(IDM_CONNECTION_STRING);

                if (_sqlConnection.State == System.Data.ConnectionState.Open)
                    return _sqlConnection;

                // Spojenù je zav?enù nebo p?eruùenù ù znovu otev?eme
                if (_sqlConnection.State != System.Data.ConnectionState.Closed)
                    _sqlConnection.Close();

                _sqlConnection.Open();
                return _sqlConnection;
            }
        }

        void IMVSynchronization.Provision(MVEntry mventry)
        {
            //
            // TODO: Remove this throw statement if you implement this method
            //
            //throw new EntryPointNotImplementedException();
            if (mventry.ObjectType == "person")
            {
                createADUser(mventry);
                createEasyIDM(mventry);
                createGISUser(mventry);

                //if (mventry["add,userName,string,,ch\\ch"])
            }
            if (mventry.ObjectType == "application-role")
            {
                if (mventry["placedAd"].IsPresent && mventry["placedAd"].BooleanValue)
                {
                    createADGroup(mventry);
                }
                createEasyIDMAppGroup(mventry);
            }
            if (mventry.ObjectType == "business-role")
            {
                createEasyIDMBusGroup(mventry);
                createADGroup(mventry);

            }
            if (mventry.ObjectType == "QI_Permissions")
            {
                ConnectedMA ma = mventry.ConnectedMAs["IDM2QI_PRAVA"];
                if (ma.Connectors.Count == 0)
                {
                    if (!mventry["workPositionCode"].IsPresent || !mventry["workType"].IsPresent) return;
                    CSEntry csEntry = ma.Connectors.StartNewConnector("Cast");
                    //csEntry["domain"].StringValue = "chevak";

                    csEntry.DN = ma.CreateDN(mventry["workPositionCode"].StringValue + "+" + mventry["employeeID"].StringValue);
                    csEntry["workPositionCode"].StringValue = mventry["workPositionCode"].StringValue;
                    csEntry["workType"].StringValue = mventry["employeeID"].StringValue;
                    csEntry.CommitNewConnector();
                }
            }


            //createADUser(mventry);
        }

        bool IMVSynchronization.ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            //
            // TODO: Add MV deletion logic here
            //
            throw new EntryPointNotImplementedException();
        }

        private void createADGroup(MVEntry mventry)
        {
            //DirectoryEntry rootDSE = new DirectoryEntry("LDAP://RootDSE");
            //string root = rootDSE.Properties["DefaultNamingContext"].Value.ToString();
            ConnectedMA ma = mventry.ConnectedMAs["chevak.cz"];
            if (!mventry["AccountName"].IsPresent || !mventry["adDN"].IsPresent) return;
            if (mventry["adDN"].StringValue.ToLower().Contains("easyidmdemo,dc=cz")) return;

            if (ma.Connectors.Count == 0)
            {

                if (!isSamInAD(mventry["AccountName"].Value))
                {
                    CSEntry cs = ma.Connectors.StartNewConnector("group");
                    cs.DN = ma.CreateDN(mventry["adDN"].StringValue);
                    cs["samAccountName"].StringValue = mventry["AccountName"].StringValue;
                    cs.CommitNewConnector();
                }
            }
            else
            {
                if (mventry["adDN"].IsPresent)
                {
                    CSEntry cs = ma.Connectors.ByIndex[0];
                    cs.DN = ma.CreateDN(mventry["adDN"].StringValue);
                }

            }
        }
        private void createADUser(MVEntry mventry)
        {
            //DirectoryEntry rootDSE = new DirectoryEntry("LDAP://RootDSE");
            //string root = rootDSE.Properties["DefaultNamingContext"].Value.ToString();
            ConnectedMA ma = mventry.ConnectedMAs["chevak.cz"];
            if (!mventry["AccountName"].IsPresent || !mventry["adDN"].IsPresent) return;
            if (ma.Connectors.Count == 0)
            {

                if (!isSamInAD(mventry["AccountName"].Value))
                {
                    if (mventry["placedAd"].IsPresent && mventry["placedAd"].BooleanValue)
                    {
                        CSEntry cs = ma.Connectors.StartNewConnector("user");
                        cs.DN = ma.CreateDN(mventry["adDN"].StringValue);


                        if (mventry["password"].IsPresent)
                        {
                            cs["unicodePwd"].Value = mventry["password"].Value;
                        }

                        cs["userAccountControl"].IntegerValue = 512;
                        // cs["pwdLastSet"].IntegerValue = -1;

                        cs.CommitNewConnector();

                    }
                    else
                    {

                    }
                }
            }
            else
            {
                if (mventry["adDN"].IsPresent)
                {
                    CSEntry cs = ma.Connectors.ByIndex[0];
                    cs.DN = ma.CreateDN(mventry["adDN"].StringValue);
                }

            }
        }
        private void createEasyIDMAppGroup(MVEntry mventry)
        {
            ConnectedMA EIDMMA = mventry.ConnectedMAs["EIDMMA"];
            if (EIDMMA.Connectors.Count == 0 && mventry["DisplayName"].IsPresent && mventry["workPositionCode"].IsPresent)
            {
                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("ApplicationRole");
                //csEntry["domain"].StringValue = "chevak";
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["name"].StringValue = mventry["DisplayName"].StringValue;
                csEntry["externalRoleCode"].StringValue = mventry["workPositionCode"].StringValue;
                csEntry["application"].StringValue = "262d4441-19e1-41b3-8723-2ecf99e4bc94";
                csEntry["groupDomain"].StringValue = "600";
                csEntry["groupStatus"].StringValue = "22";
                csEntry["groupType"].StringValue = "19";
                csEntry["isActive"].BooleanValue = true;
                csEntry["application"].StringValue = "262d4441-19e1-41b3-8723-2ecf99e4bc94";
                csEntry["owners"].Values.Add("3C969ECA-C12F-4DBC-8489-46CEB41D3FD7");
                csEntry["environment"].StringValue = "602";
                csEntry["nestingRule"].StringValue = "{\"and\":[{\"workplaceCode\":{\"eq\":\"" + mventry["workPositionCode"].StringValue + "\"}}]}";
                csEntry["nestingDirection"].StringValue = "51";
                //add,externalRoleCode,string,,002
                // csEntry["iDMObjectTypeId"].Value = "4";
                csEntry.CommitNewConnector();
            }
            else if (EIDMMA.Connectors.Count == 0 && mventry["DisplayName"].IsPresent && mventry["applicationCode"].IsPresent && mventry["applicationCode"].StringValue == "GIS" && mventry["applicationModuleCode"].IsPresent)
            {
                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("ApplicationRole");
                //csEntry["domain"].StringValue = "chevak";
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["name"].StringValue = mventry["DisplayName"].StringValue;
                csEntry["externalRoleCode"].StringValue = mventry["DisplayName"].StringValue;
                csEntry["groupDomain"].StringValue = "600";
                csEntry["groupStatus"].StringValue = "22";
                csEntry["groupType"].StringValue = "19";
                csEntry["isActive"].BooleanValue = true;
                csEntry["owners"].Values.Add("3C969ECA-C12F-4DBC-8489-46CEB41D3FD7");
                csEntry["environment"].StringValue = "602";
                //csEntry["nestingRule"].StringValue = "{\"and\":[{\"workplaceCode\":{\"eq\":\"" + mventry["workPositionCode"].StringValue + "\"}}]}";
                //csEntry["nestingDirection"].StringValue = "51";
                csEntry["techname"].StringValue = mventry["DisplayName"].StringValue;


                if (mventry["applicationModuleCode"].StringValue.ToLower() == "evstan")
                {
                    csEntry["application"].StringValue = "d9af8745-16b7-4d8b-a0d5-bf3f91e566bc";

                }
                else if (mventry["applicationModuleCode"].StringValue.ToLower() == "igis")
                {
                    csEntry["application"].StringValue = "5fa9e222-0d46-4b1b-bb42-c9cac2f0e8b7";

                }
                else if (mventry["applicationModuleCode"].StringValue.ToLower() == "ivis")
                {
                    csEntry["application"].StringValue = "40c75f91-e718-4dc8-b9a5-0ddaacc98422";
                }

                //add,externalRoleCode,string,,002
                // csEntry["iDMObjectTypeId"].Value = "4";
                csEntry.CommitNewConnector();
            }
        }
        private void createEasyIDMBusGroup(MVEntry mventry)
        {
            ConnectedMA EIDMMA = mventry.ConnectedMAs["EIDMMA"];
            if (EIDMMA.Connectors.Count == 0 && mventry["DisplayName"].IsPresent && mventry["workPositionCode"].IsPresent)
            {
                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("BusinessRole");
                //csEntry["domain"].StringValue = "chevak";
                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;
                csEntry["name"].StringValue = mventry["DisplayName"].StringValue;
                csEntry["workplaceCode"].StringValue = mventry["workPositionCode"].StringValue;
                csEntry["groupDomain"].StringValue = "600";
                csEntry["groupStatus"].StringValue = "22";
                csEntry["isActive"].BooleanValue = true;
                csEntry["owners"].Values.Add("3C969ECA-C12F-4DBC-8489-46CEB41D3FD7");
                csEntry["businessRoleType"].StringValue = "52";
                csEntry["castType"].StringValue = "41";

                /*
                string arGuid = getARGUID(mventry["workPositionCode"].StringValue);
               // File.AppendAllText(@"C:\Temp\log.txt", "arGuid:" + arGuid + Environment.NewLine);
                if (string.IsNullOrEmpty(arGuid))
                {
                    throw new Exception("Nenalezena AR pro workPositionCode:" + mventry["workPositionCode"].StringValue);
                }
               
                ReferenceValue roleRef = EIDMMA.CreateDN(arGuid);
                 
                //                File.AppendAllText(@"C:\Temp\log.txt", "roleRef:" + roleRef.ToString() + Environment.NewLine);

                csEntry["AppRole"].ReferenceValue = roleRef;
                */
                //csEntry["AutoCastRule"].StringValue = "{ \"and\":[{ \"computedAccountStatus\":{ \"eq\":\"Aktivnù\"} }]}";
                csEntry["AutoCastRule"].StringValue = "{\"and\":[{\"workPositionCode\":{\"eq\":\"" + mventry["workPositionCode"].StringValue + "\"}},{\"computedAccountStatus\":{\"neq\":\"Zruùenù\"}}]}";

                //                    add,castType,string,,41
                //                csEntry["environment"].StringValue = "602";

                //add,externalRoleCode,string,,002
                // csEntry["iDMObjectTypeId"].Value = "4";
                csEntry.CommitNewConnector();
            }
        }

        private void createGISUser(MVEntry mventry)
        {
            ConnectedMA GISMA = mventry.ConnectedMAs["GIS"];
            if (GISMA.Connectors.Count == 0 && mventry["AccountName"].IsPresent && mventry["gisEnabled"].IsPresent && mventry["gisEnabled"].BooleanValue)
            {
                string username    = mventry["AccountName"].StringValue.Trim();
                string permissions = GetGISPermissions(username);

                // Bez oprùvn?nù uùivatele nevytvù?ùme ù API by vrùtilo chybu
                if (string.IsNullOrEmpty(permissions)) return;

                CSEntry csEntry = GISMA.Connectors.StartNewConnector("User");
                csEntry["userName"].StringValue    = username;
                csEntry["permissions"].StringValue = permissions;
                csEntry.CommitNewConnector();
            }
        }

        /// <summary>
        /// Vrùtù seznam rolù pro danùho uùivatele (i.TechName) z iDM databùze
        /// jako ?ùrkami odd?lenù ?et?zec permName hodnot pro GIS aplikaci.
        /// Vrùtù prùzdnù ?et?zec pokud uùivatel nemù ùùdnù GIS oprùvn?nù.
        /// </summary>
        public static string GetGISPermissions(string username)
        {
            if (string.IsNullOrEmpty(username)) return string.Empty;

            const string sql = @"
                SELECT r.TechName AS rolename
                FROM dbo.IDMObjects AS r
                FULL OUTER JOIN dbo.IDMObjects AS i
                    INNER JOIN dbo.IDMObjectReadStore
                        ON i.IDMObjectId = dbo.IDMObjectReadStore.IdmObjectId
                    FULL OUTER JOIN
                        (SELECT IDMObjectId, applicationCode
                         FROM (
                             SELECT i.IDMObjectId,
                                    JSON_VALUE(rsApp.JsonData, '$.applicationCodeAP') AS applicationCode
                             FROM dbo.IDMObjects AS i
                             LEFT OUTER JOIN dbo.IDMObjectReadStore AS rs
                                 ON i.IDMObjectId = rs.IdmObjectId
                             LEFT OUTER JOIN dbo.RoleAccesses AS ra
                                 ON ra.ApplicationRoleId = i.IDMObjectId
                             LEFT OUTER JOIN dbo.IDMObjectReadStore AS rsApp
                                 ON ra.ResourceId = rsApp.IdmObjectId
                             WHERE i.IDMObjectTypeId = 6
                         ) AS x
                        ) AS y
                    RIGHT OUTER JOIN dbo.Casts AS c
                        ON y.IDMObjectId = c.RoleId
                    ON c.IdentityId = i.IDMObjectId
                ON r.IDMObjectId = c.RoleId
                WHERE y.applicationCode = 'GIS'
                  AND c.IsArchived  = 0
                  AND c.IsValid     = 1
                  AND LEN(r.TechName) < 30
                  AND LEN(i.TechName) < 30
                  AND (c.ValidTo > GETDATE() OR c.ValidTo IS NULL)
                  AND i.TechName = @username";

            List<string> roles = new List<string>();

            try
            {
                SqlConnection conn = EnsureConnection();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add(new SqlParameter("@username", username));
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string roleName = reader["rolename"] as string;
                            if (!string.IsNullOrEmpty(roleName))
                                roles.Add(roleName.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(@"C:\Temp\GIS_log.txt",
                    DateTime.Now + " GetGISPermissions ERROR [" + username + "]: "
                    + ex.Message + Environment.NewLine);
            }

            return string.Join(",", roles.ToArray());
        }

        private void createEasyIDM(MVEntry mventry)
        {
            ConnectedMA EIDMMA = mventry.ConnectedMAs["EIDMMA"];
            if (EIDMMA.Connectors.Count == 0 && mventry["firstName"].IsPresent && mventry["lastName"].IsPresent && mventry["employeeType"].IsPresent && mventry["employeeID"].IsPresent)
            {
                CSEntry csEntry = EIDMMA.Connectors.StartNewConnector("Identity");
                //csEntry["Name"].Value = mventry["AccountName"].Value;

                string strGUID = Guid.NewGuid().ToString().ToUpper();
                csEntry.DN = EIDMMA.CreateDN(strGUID);
                csEntry["idmObjectId"].StringValue = strGUID;

                csEntry["domain"].StringValue = "chevak";
                csEntry["organization"].StringValue = "chevak";

                csEntry["iDMObjectTypeId"].Value = "4";
                csEntry["employeeType"].StringValue = mventry["employeeType"].StringValue;

                csEntry["accountStatus"].StringValue = "26";
                csEntry["placedAd"].BooleanValue = true;
                csEntry["hasCalculateItemsIdentity"].BooleanValue = true;



                csEntry.CommitNewConnector();
            }
        }

        public static string getARGUID(string workPositionCode)
        {
            //File.AppendAllText(@"C:\Temp\log.txt", "Hledani:" + workPositionCode);
            string EIDMGuid = null;
            MVEntry[] mvEntries = Microsoft.MetadirectoryServices.Utils.FindMVEntries("workPositionCode", workPositionCode);
            // File.AppendAllText(@"C:\Temp\log.txt", "nalezeno:" + mvEntries.Length);
            foreach (MVEntry smventry in mvEntries)
            {
                // File.AppendAllText(@"C:\Temp\log.txt", "ObjectType:" + smventry.ObjectType);
                if (smventry.ObjectType == "application-role")
                    if (smventry["EIDM_GUID"].IsPresent)
                    {
                        //File.AppendAllText(@"C:\Temp\log.txt", "Value:" + smventry["EIDM_GUID"].StringValue);
                        EIDMGuid = smventry["EIDM_GUID"].StringValue;
                        return EIDMGuid;
                    }
            }

            return null;
        }

        public static bool isSamInAD(string sam)
        {
            DirectoryEntry rootDSE = new DirectoryEntry("LDAP://RootDSE");
            string domainDN = rootDSE.Properties["DefaultNamingContext"].Value.ToString();
            DirectoryEntry aDEntry = new DirectoryEntry("LDAP://" + domainDN);

            DirectorySearcher aDSearcher = new DirectorySearcher(aDEntry);

            aDSearcher.Filter = string.Format("(|(sAMAccountName={0})(employeeID={0}))", sam.Trim());

            aDSearcher.SearchScope = SearchScope.Subtree;
            return aDSearcher.FindAll().Count >= 1;
        }



    }
}
