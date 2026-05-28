using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.MetadirectoryServices;

namespace FimSync_Ezma
{
    /// <summary>
    /// MIM 2016 ECMA2 Extensible Connector pro iDM REST API (EasyiDM).
    ///
    /// Objektové typy:
    ///   User  – uživatelé (anchor: userID / GUID z API)
    ///   Group – oprávnění jako skupiny (anchor: permName)
    ///
    /// Mapování API:
    ///   Import User   → GET /GetAllUser
    ///   Import Group  → GET /GetAllPerm  (+  členové z /GetAllUser)
    ///   User Add      → POST /CreateUser
    ///   User Update   → PUT  /UpdateUser  (prázdné permissions = blokace)
    ///   User Delete   → DELETE /DeleteUser/{userID}
    ///   Group member Add/Delete → GET /GetUserIdPerm + PUT /UpdateUser na dotčeném uživateli
    /// </summary>
    public class EzmaExtension :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2CallImport,
        IMAExtensible2CallExport
    {
        private const int ImportPageSize = 100;

        private string _baseUrl;
        private string _apiUser;
        private string _apiPassword;
        private HttpClient _httpClient;
        private List<CSEntryChange> _importBuffer;
        private int _importIndex;

        public EzmaExtension() { }

        // =====================================================================
        // IMAExtensible2GetCapabilities
        // =====================================================================

        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities caps = new MACapabilities();
                caps.SupportImport          = true;
                caps.SupportExport          = true;
                caps.ObjectRename           = true;
                caps.FullExport             = false;
                caps.DeltaImport            = false;
                // AttributeReplace umožňuje dostat Add/Delete changes pro multi-valued member
                caps.ExportType             = MAExportType.AttributeReplace;
                caps.DistinguishedNameStyle = MADistinguishedNameStyle.None;
                caps.Normalizations         = MANormalizations.None;
                return caps;
            }
        }

        // =====================================================================
        // IMAExtensible2GetSchema
        // =====================================================================

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            Schema schema = Schema.Create();

            // ------------------------------------------------------------------
            // Typ User
            // ------------------------------------------------------------------
            SchemaType userType = SchemaType.Create("User", false);

            // Anchor – interní GUID vrácený API při CreateUser
            userType.Attributes.Add(
                SchemaAttribute.CreateAnchorAttribute(
                    "userID", AttributeType.String, AttributeOperation.ImportOnly));

            // AD uživatelské jméno (v API kódováno Base64, zde jako čitelný řetězec)
            userType.Attributes.Add(
                SchemaAttribute.CreateSingleValuedAttribute(
                    "userName", AttributeType.String, AttributeOperation.ImportExport));

            // Přiřazená oprávnění – multivalued; prázdná sada = blokovaný uživatel
            userType.Attributes.Add(
                SchemaAttribute.CreateMultiValuedAttribute(
                    "permissions", AttributeType.String, AttributeOperation.ImportExport));

            schema.Types.Add(userType);

            // ------------------------------------------------------------------
            // Typ Group  (oprávnění jako skupiny)
            // ------------------------------------------------------------------
            SchemaType groupType = SchemaType.Create("Group", false);

            // Anchor – jméno oprávnění (permName), konstantní identifikátor v API
            groupType.Attributes.Add(
                SchemaAttribute.CreateAnchorAttribute(
                    "permName", AttributeType.String, AttributeOperation.ImportOnly));

            // Popis oprávnění (permDesc)
            groupType.Attributes.Add(
                SchemaAttribute.CreateSingleValuedAttribute(
                    "permDesc", AttributeType.String, AttributeOperation.ImportOnly));

            // Členové skupiny – reference na User objekty (hodnota = userID)
            groupType.Attributes.Add(
                SchemaAttribute.CreateMultiValuedAttribute(
                    "member", AttributeType.Reference, AttributeOperation.ImportExport));

            schema.Types.Add(groupType);

            return schema;
        }

        // =====================================================================
        // IMAExtensible2GetParameters
        // =====================================================================

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> list = new List<ConfigParameterDefinition>();
            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    "BaseUrl", string.Empty,
                    "https://cheapp01.chevak.cz:4430/iDMApiTest/api"));
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    "ApiUser", string.Empty));
                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(
                    "ApiPassword", string.Empty));
            }
            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            if (page == ConfigParameterPage.Connectivity)
            {
                if (configParameters.Contains("BaseUrl") &&
                    string.IsNullOrEmpty(configParameters["BaseUrl"].Value))
                    return new ParameterValidationResult(
                        ParameterValidationResultCode.Failure,
                        "BaseUrl", "Parametr BaseUrl nesmí být prázdný.");
            }
            return new ParameterValidationResult(
                ParameterValidationResultCode.Success, string.Empty, string.Empty);
        }

        // =====================================================================
        // IMAExtensible2CallImport
        // =====================================================================

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep importRunStep)
        {
            LoadConfig(configParameters);
            InitHttpClient();

            // Načteme raw data uživatelů jednou – potřebujeme je pro User i Group import
            List<Dictionary<string, object>> rawUsers = FetchRawUsers();

            _importBuffer = new List<CSEntryChange>();
            _importBuffer.AddRange(ConvertRawUsersToCSEntries(rawUsers));
            _importBuffer.AddRange(FetchAllGroups(rawUsers));
            _importIndex = 0;

            return new OpenImportConnectionResults();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            GetImportEntriesResults results = new GetImportEntriesResults();
            List<CSEntryChange> batch       = new List<CSEntryChange>();

            while (_importIndex < _importBuffer.Count && batch.Count < ImportPageSize)
                batch.Add(_importBuffer[_importIndex++]);

            results.CSEntries    = batch;
            results.MoreToImport = _importIndex < _importBuffer.Count;
            return results;
        }

        public CloseImportConnectionResults CloseImportConnection(
            CloseImportConnectionRunStep importRunStep)
        {
            _importBuffer = null;
            DisposeHttpClient();
            return new CloseImportConnectionResults();
        }

        public int ImportDefaultPageSize { get { return ImportPageSize; } }
        public int ImportMaxPageSize     { get { return 10000; } }

        // =====================================================================
        // IMAExtensible2CallExport
        // =====================================================================

        public void OpenExportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenExportConnectionRunStep exportRunStep)
        {
            LoadConfig(configParameters);
            InitHttpClient();
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            PutExportEntriesResults results = new PutExportEntriesResults();

            foreach (CSEntryChange csentry in csentries)
            {
                CSEntryChangeResult changeResult;
                try
                {
                    if (csentry.ObjectType == "Group")
                    {
                        changeResult = ExportGroup(csentry);
                    }
                    else
                    {
                        switch (csentry.ObjectModificationType)
                        {
                            case ObjectModificationType.Add:
                                changeResult = ExportAdd(csentry);
                                break;
                            case ObjectModificationType.Replace:
                            case ObjectModificationType.Update:
                                changeResult = ExportUpdate(csentry);
                                break;
                            case ObjectModificationType.Delete:
                                changeResult = ExportDelete(csentry);
                                break;
                            default:
                                changeResult = CSEntryChangeResult.Create(
                                    csentry.Identifier, null,
                                    MAExportError.ExportErrorCustomContinueRun,
                                    "UnsupportedOperation",
                                    csentry.ObjectModificationType.ToString());
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    changeResult = CSEntryChangeResult.Create(
                        csentry.Identifier, null,
                        MAExportError.ExportErrorCustomContinueRun,
                        "Exception", ex.Message);
                }

                results.CSEntryChangeResults.Add(changeResult);
            }

            return results;
        }

        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {
            DisposeHttpClient();
        }

        public int ExportDefaultPageSize { get { return 100; } }
        public int ExportMaxPageSize     { get { return 10000; } }

        // =====================================================================
        // Export User – mapování na API volání
        // =====================================================================

        private CSEntryChangeResult ExportAdd(CSEntryChange csentry)
        {
            string userName    = GetSingleValue(csentry, "userName");
            List<string> perms = GetMultiValues(csentry, "permissions");

            Dictionary<string, object> body = new Dictionary<string, object>();
            body.Add("userNameBASE64", Base64Encode(userName));
            body.Add("permissions",    string.Join(",", perms.ToArray()));

            string responseJson = CallApi("POST", _baseUrl + "/CreateUser", body);
            Dictionary<string, object> resp = ParseJsonObject(responseJson);
            string resultMsg = GetDictString(resp, "result");

            if (!resultMsg.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return CSEntryChangeResult.Create(
                    csentry.Identifier, null,
                    MAExportError.ExportErrorCustomContinueRun, "ApiError", resultMsg);

            // API vrátí nové userID – vracíme ho jako anchor (AttributeChange)
            string newUserId = GetDictString(resp, "userID");
            List<AttributeChange> anchorAttribs = new List<AttributeChange>();
            anchorAttribs.Add(AttributeChange.CreateAttributeAdd("userID", newUserId));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorAttribs, MAExportError.Success);
        }

        private CSEntryChangeResult ExportUpdate(CSEntryChange csentry)
        {
            string userID = GetAnchorValue(csentry, "userID");

            Dictionary<string, object> body = new Dictionary<string, object>();
            body.Add("userID",         userID);
            body.Add("userNameBASE64", string.Empty);
            body.Add("permissions",    string.Empty);

            AttributeChange userNameChange = FindAttributeChange(csentry, "userName");
            if (userNameChange != null)
                body["userNameBASE64"] = Base64Encode(GetFirstAddValue(userNameChange));

            // Plná sada oprávnění (AttributeReplace: Replace přijde jako full-set Add hodnot)
            AttributeChange permChange = FindAttributeChange(csentry, "permissions");
            if (permChange != null)
                body["permissions"] = string.Join(",", GetAddValues(permChange).ToArray());

            string responseJson = CallApi("PUT", _baseUrl + "/UpdateUser", body);
            Dictionary<string, object> resp = ParseJsonObject(responseJson);
            string resultMsg = GetDictString(resp, "result");

            if (!resultMsg.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return CSEntryChangeResult.Create(
                    csentry.Identifier, null,
                    MAExportError.ExportErrorCustomContinueRun, "ApiError", resultMsg);

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private CSEntryChangeResult ExportDelete(CSEntryChange csentry)
        {
            string userID = GetAnchorValue(csentry, "userID");

            string responseJson = CallApi(
                "DELETE", _baseUrl + "/DeleteUser/" + Uri.EscapeDataString(userID), null);
            Dictionary<string, object> resp = ParseJsonObject(responseJson);
            string resultMsg = GetDictString(resp, "result");

            if (!resultMsg.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return CSEntryChangeResult.Create(
                    csentry.Identifier, null,
                    MAExportError.ExportErrorCustomContinueRun, "ApiError", resultMsg);

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        // =====================================================================
        // Export Group – správa členství v oprávněních
        // =====================================================================

        /// <summary>
        /// Group je read-only co do samotných atributů (permName/permDesc).
        /// Export se týká pouze atributu "member": přidání/odebrání uživatele ze skupiny
        /// se provede přes GET /GetUserIdPerm + PUT /UpdateUser na dotčeném uživateli.
        /// </summary>
        private CSEntryChangeResult ExportGroup(CSEntryChange csentry)
        {
            // Group nelze přidávat ani mazat přes API – pouze Update/Replace pro member
            if (csentry.ObjectModificationType == ObjectModificationType.Delete)
                return CSEntryChangeResult.Create(
                    csentry.Identifier, null,
                    MAExportError.ExportErrorCustomContinueRun,
                    "ReadOnly", "Oprávnění (Group) nelze mazat přes API.");

            if (csentry.ObjectModificationType == ObjectModificationType.Add)
                return CSEntryChangeResult.Create(
                    csentry.Identifier, null,
                    MAExportError.ExportErrorCustomContinueRun,
                    "ReadOnly", "Oprávnění (Group) nelze vytvářet přes API.");

            string permName = GetAnchorValue(csentry, "permName");
            AttributeChange memberChange = FindAttributeChange(csentry, "member");

            if (memberChange == null)
                return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);

            foreach (ValueChange vc in memberChange.ValueChanges)
            {
                string userID = vc.Value as string ?? string.Empty;
                if (string.IsNullOrEmpty(userID)) continue;

                // Načteme aktuální oprávnění uživatele
                List<string> currentPerms = GetUserCurrentPermissions(userID);

                if (vc.ModificationType == ValueModificationType.Add)
                {
                    if (!currentPerms.Contains(permName))
                        currentPerms.Add(permName);
                }
                else if (vc.ModificationType == ValueModificationType.Delete)
                {
                    currentPerms.Remove(permName);
                }
                else
                {
                    continue;
                }

                // Zapíšeme upravenou sadu oprávnění zpět
                Dictionary<string, object> body = new Dictionary<string, object>();
                body.Add("userID",         userID);
                body.Add("userNameBASE64", string.Empty);
                body.Add("permissions",    string.Join(",", currentPerms.ToArray()));

                string responseJson = CallApi("PUT", _baseUrl + "/UpdateUser", body);
                Dictionary<string, object> resp = ParseJsonObject(responseJson);
                string resultMsg = GetDictString(resp, "result");

                if (!resultMsg.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    return CSEntryChangeResult.Create(
                        csentry.Identifier, null,
                        MAExportError.ExportErrorCustomContinueRun,
                        "ApiError", "User " + userID + ": " + resultMsg);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        // =====================================================================
        // Import – načtení uživatelů z API
        // =====================================================================

        /// <summary>
        /// Vrátí surová JSON data uživatelů z /GetAllUser pro opakované použití.
        /// </summary>
        private List<Dictionary<string, object>> FetchRawUsers()
        {
            string json = _httpClient.GetStringAsync(_baseUrl + "/GetAllUser").Result;
            JavaScriptSerializer js = new JavaScriptSerializer();
            List<Dictionary<string, object>> users =
                js.Deserialize<List<Dictionary<string, object>>>(json)
                ?? new List<Dictionary<string, object>>();
            return users;
        }

        private List<CSEntryChange> ConvertRawUsersToCSEntries(
            List<Dictionary<string, object>> rawUsers)
        {
            List<CSEntryChange> entries = new List<CSEntryChange>();
            foreach (Dictionary<string, object> user in rawUsers)
            {
                string userID      = GetDictString(user, "userID");
                string userNameB64 = GetDictString(user, "userNameBASE64");

                CSEntryChange cs = CSEntryChange.Create();
                cs.ObjectModificationType = ObjectModificationType.Add;
                cs.ObjectType             = "User";

                cs.AnchorAttributes.Add(AnchorAttribute.Create("userID", userID));
                cs.AttributeChanges.Add(
                    AttributeChange.CreateAttributeAdd("userName", Base64Decode(userNameB64)));

                List<object> permNames = new List<object>();
                if (user.ContainsKey("permissions") &&
                    user["permissions"] is System.Collections.ArrayList)
                {
                    System.Collections.ArrayList permArray =
                        (System.Collections.ArrayList)user["permissions"];
                    foreach (object item in permArray)
                    {
                        if (item is Dictionary<string, object>)
                        {
                            Dictionary<string, object> perm = (Dictionary<string, object>)item;
                            if (perm.ContainsKey("permName") && perm["permName"] != null)
                                permNames.Add(perm["permName"]);
                        }
                    }
                }

                if (permNames.Count > 0)
                    cs.AttributeChanges.Add(
                        AttributeChange.CreateAttributeAdd("permissions", permNames));

                entries.Add(cs);
            }
            return entries;
        }

        // =====================================================================
        // Import – načtení skupin (oprávnění) z API
        // =====================================================================

        /// <summary>
        /// Načte skupiny z /GetAllPerm a pro každou skupinu sestaví seznam členů
        /// zkřížením s daty z /GetAllUser (která jsou předána jako rawUsers).
        /// </summary>
        private List<CSEntryChange> FetchAllGroups(
            List<Dictionary<string, object>> rawUsers)
        {
            string json = _httpClient.GetStringAsync(_baseUrl + "/GetAllPerm").Result;
            JavaScriptSerializer js = new JavaScriptSerializer();
            List<Dictionary<string, object>> allPerms =
                js.Deserialize<List<Dictionary<string, object>>>(json)
                ?? new List<Dictionary<string, object>>();

            // Sestavíme index: permName → seznam userID členů
            Dictionary<string, List<object>> memberIndex =
                new Dictionary<string, List<object>>();

            foreach (Dictionary<string, object> user in rawUsers)
            {
                string userID = GetDictString(user, "userID");
                if (string.IsNullOrEmpty(userID)) continue;

                if (!user.ContainsKey("permissions") ||
                    !(user["permissions"] is System.Collections.ArrayList))
                    continue;

                System.Collections.ArrayList permArray =
                    (System.Collections.ArrayList)user["permissions"];

                foreach (object item in permArray)
                {
                    if (!(item is Dictionary<string, object>)) continue;
                    Dictionary<string, object> perm = (Dictionary<string, object>)item;
                    string pName = GetDictString(perm, "permName");
                    if (string.IsNullOrEmpty(pName)) continue;

                    if (!memberIndex.ContainsKey(pName))
                        memberIndex.Add(pName, new List<object>());
                    memberIndex[pName].Add(userID);
                }
            }

            // Vytvoříme CSEntryChange pro každé oprávnění
            List<CSEntryChange> entries = new List<CSEntryChange>();
            foreach (Dictionary<string, object> perm in allPerms)
            {
                string permName = GetDictString(perm, "permName");
                string permDesc = GetDictString(perm, "permDesc");

                if (string.IsNullOrEmpty(permName)) continue;

                CSEntryChange cs = CSEntryChange.Create();
                cs.ObjectModificationType = ObjectModificationType.Add;
                cs.ObjectType             = "Group";

                cs.AnchorAttributes.Add(AnchorAttribute.Create("permName", permName));
                cs.AttributeChanges.Add(
                    AttributeChange.CreateAttributeAdd("permDesc", permDesc));

                if (memberIndex.ContainsKey(permName) && memberIndex[permName].Count > 0)
                    cs.AttributeChanges.Add(
                        AttributeChange.CreateAttributeAdd("member", memberIndex[permName]));

                entries.Add(cs);
            }

            return entries;
        }

        // =====================================================================
        // HTTP klient
        // =====================================================================

        private void LoadConfig(KeyedCollection<string, ConfigParameter> config)
        {
            _baseUrl     = config["BaseUrl"].Value.TrimEnd('/');
            _apiUser     = config["ApiUser"].Value;
            _apiPassword = SecureStringToString(config["ApiPassword"].SecureValue);
        }

        private void InitHttpClient()
        {
            // Povolíme TLS 1.2 a akceptujeme self-signed certifikáty testovacího serveru
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072 |   // Tls12
                (SecurityProtocolType)768  |   // Tls11
                SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback =
                delegate(object sender,
                         System.Security.Cryptography.X509Certificates.X509Certificate cert,
                         System.Security.Cryptography.X509Certificates.X509Chain chain,
                         System.Net.Security.SslPolicyErrors errors)
                {
                    return true;
                };

            _httpClient = new HttpClient();
            string credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes(_apiUser + ":" + _apiPassword));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private void DisposeHttpClient()
        {
            if (_httpClient != null)
            {
                _httpClient.Dispose();
                _httpClient = null;
            }
        }

        private string CallApi(string method, string url, Dictionary<string, object> body)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            HttpResponseMessage response;

            switch (method.ToUpperInvariant())
            {
                case "POST":
                    StringContent postContent = new StringContent(
                        js.Serialize(body), Encoding.UTF8, "application/json");
                    response = _httpClient.PostAsync(url, postContent).Result;
                    break;
                case "PUT":
                    StringContent putContent = new StringContent(
                        js.Serialize(body), Encoding.UTF8, "application/json");
                    response = _httpClient.PutAsync(url, putContent).Result;
                    break;
                case "DELETE":
                    response = _httpClient.DeleteAsync(url).Result;
                    break;
                default:
                    response = _httpClient.GetAsync(url).Result;
                    break;
            }

            return response.Content.ReadAsStringAsync().Result;
        }

        private static Dictionary<string, object> ParseJsonObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
            JavaScriptSerializer js = new JavaScriptSerializer();
            Dictionary<string, object> result =
                js.Deserialize<Dictionary<string, object>>(json);
            return result ?? new Dictionary<string, object>();
        }

        // =====================================================================
        // Pomocné metody – oprávnění uživatele
        // =====================================================================

        /// <summary>
        /// Načte aktuální seznam oprávnění uživatele z GET /GetUserIdPerm/{userID}.
        /// Používá se při export operacích na Group objektu.
        /// </summary>
        private List<string> GetUserCurrentPermissions(string userID)
        {
            string json = _httpClient
                .GetStringAsync(_baseUrl + "/GetUserIdPerm/" + Uri.EscapeDataString(userID))
                .Result;

            JavaScriptSerializer js = new JavaScriptSerializer();
            List<Dictionary<string, object>> perms =
                js.Deserialize<List<Dictionary<string, object>>>(json)
                ?? new List<Dictionary<string, object>>();

            List<string> result = new List<string>();
            foreach (Dictionary<string, object> p in perms)
            {
                string pName = GetDictString(p, "permName");
                if (!string.IsNullOrEmpty(pName))
                    result.Add(pName);
            }
            return result;
        }

        // =====================================================================
        // Pomocné metody – atributy
        // =====================================================================

        private static string GetSingleValue(CSEntryChange csentry, string attrName)
        {
            foreach (AttributeChange ac in csentry.AttributeChanges)
            {
                if (ac.Name != attrName) continue;
                foreach (ValueChange vc in ac.ValueChanges)
                    if (vc.ModificationType == ValueModificationType.Add)
                        return vc.Value as string ?? string.Empty;
            }
            return string.Empty;
        }

        private static List<string> GetMultiValues(CSEntryChange csentry, string attrName)
        {
            List<string> list = new List<string>();
            foreach (AttributeChange ac in csentry.AttributeChanges)
            {
                if (ac.Name != attrName) continue;
                foreach (ValueChange vc in ac.ValueChanges)
                    if (vc.ModificationType == ValueModificationType.Add)
                        list.Add(vc.Value as string ?? string.Empty);
            }
            return list;
        }

        private static AttributeChange FindAttributeChange(CSEntryChange csentry, string attrName)
        {
            foreach (AttributeChange ac in csentry.AttributeChanges)
                if (ac.Name == attrName) return ac;
            return null;
        }

        private static string GetFirstAddValue(AttributeChange ac)
        {
            foreach (ValueChange vc in ac.ValueChanges)
                if (vc.ModificationType == ValueModificationType.Add)
                    return vc.Value as string ?? string.Empty;
            return string.Empty;
        }

        private static List<string> GetAddValues(AttributeChange ac)
        {
            List<string> list = new List<string>();
            foreach (ValueChange vc in ac.ValueChanges)
                if (vc.ModificationType == ValueModificationType.Add)
                    list.Add(vc.Value as string ?? string.Empty);
            return list;
        }

        private static string GetAnchorValue(CSEntryChange csentry, string name)
        {
            foreach (AnchorAttribute aa in csentry.AnchorAttributes)
                if (aa.Name == name) return aa.Value as string ?? string.Empty;
            return string.Empty;
        }

        private static string GetDictString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null)
                return string.Empty;
            return dict[key] as string ?? string.Empty;
        }

        // =====================================================================
        // Pomocné metody – kódování
        // =====================================================================

        private static string Base64Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private static string Base64Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return string.Empty;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch { return encoded; }
        }

        private static string SecureStringToString(SecureString secureString)
        {
            if (secureString == null) return string.Empty;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }
}
