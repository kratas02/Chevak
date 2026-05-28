
using Microsoft.MetadirectoryServices;
using System;
using System.DirectoryServices;
using System.IO;
using System.Security.Cryptography;

namespace Mms_Metaverse
{
    /// <summary>
    /// Summary description for MVExtensionObject.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {

        string ADMA = "chevak.cz";
        public MVExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }

        void IMVSynchronization.Initialize()
        {
            //
            // TODO: Add initialization logic here
            //
        }

        void IMVSynchronization.Terminate()
        {
            //
            // TODO: Add termination logic here
            //
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
                //csEntry["AutoCastRule"].StringValue = "{ \"and\":[{ \"computedAccountStatus\":{ \"eq\":\"Aktivní\"} }]}";
                csEntry["AutoCastRule"].StringValue = "{\"and\":[{\"workPositionCode\":{\"eq\":\"" + mventry["workPositionCode"].StringValue + "\"}},{\"computedAccountStatus\":{\"neq\":\"Zrušený\"}}]}";

                //                    add,castType,string,,41
                //                csEntry["environment"].StringValue = "602";

                //add,externalRoleCode,string,,002
                // csEntry["iDMObjectTypeId"].Value = "4";
                csEntry.CommitNewConnector();
            }
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
