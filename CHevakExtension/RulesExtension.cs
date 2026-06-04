
using Microsoft.MetadirectoryServices;
using System;
using System.Data;

namespace Mms_ManagementAgent_MVASOLExtension
{
    /// <summary>
    /// Summary description for MAExtensionObject.
	/// </summary>
	public class MAExtensionObject : IMASynchronization
    {
        const long ADS_UF_NORMAL_ACCOUNT = 0x200;
        const long ADS_UF_ACCOUNTDISABLE = 0x2;

        public MAExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }
        void IMASynchronization.Initialize()
        {
            // Otevřeme SQL připojení pro případ, že běží pouze export bez provisioning kroku
            // (MVExtension.Initialize nemusí být voláno při standalone Export run stepu)
            Mms_Metaverse.MVExtensionObject.OpenSqlConnection();
        }

        void IMASynchronization.Terminate()
        {
            // Zavřeme SQL připojení – platí pro oba případy (s i bez provisioning kroku)
            Mms_Metaverse.MVExtensionObject.CloseSqlConnection();
        }

        bool IMASynchronization.ShouldProjectToMV(CSEntry csentry, out string MVObjectType)
        {
            //
            // TODO: Remove this throw statement if you implement this method
            //
            throw new EntryPointNotImplementedException();
        }

        DeprovisionAction IMASynchronization.Deprovision(CSEntry csentry)
        {
            //
            // TODO: Remove this throw statement if you implement this method
            //
            throw new EntryPointNotImplementedException();
        }

        bool IMASynchronization.FilterForDisconnection(CSEntry csentry)
        {
            //
            // TODO: write connector filter code
            //
            throw new EntryPointNotImplementedException();
        }

        void IMASynchronization.MapAttributesForJoin(string FlowRuleName, CSEntry csentry, ref ValueCollection values)
        {
            //
            // TODO: write join mapping code
            //
            throw new EntryPointNotImplementedException();
        }

        bool IMASynchronization.ResolveJoinSearch(string joinCriteriaName, CSEntry csentry, MVEntry[] rgmventry, out int imventry, ref string MVObjectType)
        {
            //
            // TODO: write join resolution code
            //
            throw new EntryPointNotImplementedException();
        }

        void IMASynchronization.MapAttributesForImport(string FlowRuleName, CSEntry csentry, MVEntry mventry)
        {

            switch (FlowRuleName)
            {
                case "cd.Group:permName->mv.application-role:applicationModuleCode":
                    if (csentry["permName"].StringValue.ToLower().StartsWith("evstan"))
                    {
                        mventry["applicationModuleCode"].StringValue = "EvStan";
                    }
                    else if (csentry["permName"].StringValue.ToLower().StartsWith("igis"))
                    {
                        mventry["applicationModuleCode"].StringValue = "iGIS";

                    }
                    else if (csentry["permName"].StringValue.ToLower().StartsWith("ivis"))
                    {
                        mventry["applicationModuleCode"].StringValue = "iVIS";
                    }

            break;
                case "cd.Group:permName->mv.application-role:displayName":
                    if (csentry["permName"].StringValue.ToLower().StartsWith("evstan"))
                    {
                        mventry["displayName"].StringValue = csentry["permName"].StringValue.Substring(csentry["permName"].StringValue.IndexOf('_')+1);
                    }
                    else if (csentry["permName"].StringValue.ToLower().StartsWith("igis"))
                    {
                        mventry["displayName"].StringValue = csentry["permName"].StringValue.Substring(csentry["permName"].StringValue.IndexOf('_') + 1);

                    }
                    else if (csentry["permName"].StringValue.ToLower().StartsWith("ivis"))
                    {
                        mventry["displayName"].StringValue = csentry["permName"].StringValue.Substring(csentry["permName"].StringValue.IndexOf('_') + 1);
                    }
                    break;
                case "cd.Identity:accountStatusToAD->mv.person:isEnabled":
                    //mventry["isEnabled"].BooleanValue = !Convert.ToBoolean(csentry["accountStatusToAD"].IntegerValue & 0x0002);

                    if (csentry["accountStatusToAD"].IsPresent)
                    {
                        if (csentry["accountStatusToAD"].StringValue == "512")
                            mventry["isEnabled"].BooleanValue = true;
                        else
                            mventry["isEnabled"].BooleanValue = false;
                    }
                    else
                    {
                        mventry["isEnabled"].BooleanValue = false;
                    }
                    break;

                default:
                    throw new EntryPointNotImplementedException();
            }
        }

        void IMASynchronization.MapAttributesForExport(string FlowRuleName, MVEntry mventry, CSEntry csentry)
        {

            switch (FlowRuleName)
            {
                case "cd.BusinessRole:name<-mv.business-role:displayName,workPositionCode":
                    if (mventry["workPositionCode"].IsPresent && mventry["displayName"].IsPresent)
                    {
                        csentry["name"].StringValue = mventry["displayName"].StringValue + "_" + mventry["workPositionCode"].StringValue;
                    }
                    break;
                case "cd.Identity:validFrom<-mv.person:validFrom":
                    if (mventry["validFrom"].IsPresent)
                    {
                        string[] items = mventry["validFrom"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["validFrom"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        csentry["validFrom"].Delete();
                    }

                    break;
                case "cd.Identity:validTo<-mv.person:validTo":
                    if (mventry["validTo"].IsPresent)
                    {
                        string[] items = mventry["validTo"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["validTo"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        if (mventry["hrIdentity"].IsPresent && mventry["hrIdentity"].BooleanValue && mventry["hrIdentity"].LastContributingMA.Name == "EIDMMA")
                        {
                            if (!csentry["validTo"].IsPresent)
                            {
                                csentry["validTo"].StringValue = DateTime.Now.AddDays(-1).ToString("yyyy-MM-ddT00:00:00");
                            }
                        }
                        else
                        {
                            csentry["validTo"].Delete();
                        }
                        
                    }

                    break;
                case "cd.Identity:inFrom<-mv.person:inFrom":
                    if (mventry["inFrom"].IsPresent)
                    {
                        string[] items = mventry["inFrom"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["inFrom"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        csentry["inFrom"].Delete();
                    }

                    break;
                case "cd.Identity:outFrom<-mv.person:outFrom":
                    if (mventry["outFrom"].IsPresent)
                    {
                        string[] items = mventry["outFrom"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["outFrom"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        csentry["outFrom"].Delete();
                    }

                    break;
                case "cd.Identity:validZFrom<-mv.person:validZFrom":
                    if (mventry["validZFrom"].IsPresent)
                    {
                        string[] items = mventry["validZFrom"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["validZFrom"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        csentry["validZFrom"].Delete();
                    }

                    break;
                case "cd.Identity:validZTo<-mv.person:validZTo":
                    if (mventry["validZTo"].IsPresent)
                    {
                        string[] items = mventry["validZTo"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["validZTo"].StringValue = items[2] + "-" + items[1] + "-" + items[0] + "T00:00:00";
                    }
                    else
                    {
                        csentry["validZTo"].Delete();
                    }

                    break;
                case "cd.BusinessRole:AutoCastRule<-mv.business-role:workPositionCode":
                    if (mventry["workPositionCode"].IsPresent)
                        csentry["AutoCastRule"].StringValue = "{\"and\":[{\"workPositionCode\":{\"contains\":\"" + mventry["workPositionCode"].StringValue + "\"}},{\"computedAccountStatus\":{\"neq\":\"Zrušený\"}}]}";

                    break;
                case "groupStatus<-workPositionCode":

                    //modify,groupStatus,string,22,25
                    if (mventry["workPositionCode"].IsPresent)
                    {
                        //if (csentry["groupStatus"].StringValue == "25")
                        csentry["groupStatus"].StringValue = "22";
                    }
                    else
                    {
                        csentry["groupStatus"].StringValue = "25";
                    }
                    break;
                case "cd.user:userAccountControl<-mv.person:isEnabled":
                    {
                        if (csentry["userAccountControl"].IsPresent && (mventry["isActive"].IsPresent))
                        {
                            long currentValue = csentry["userAccountControl"].IntegerValue;
                            if (mventry["isActive"].BooleanValue)
                                csentry["userAccountControl"].IntegerValue = (currentValue & ~ADS_UF_ACCOUNTDISABLE); //enable
                            else
                                csentry["userAccountControl"].IntegerValue = (currentValue | ADS_UF_ACCOUNTDISABLE); //disable
                        }
                        break;
                    }
                case "cd.Identity:jobTitle<-mv.person:jobTitle":
                    if (mventry["jobTitle"].IsPresent)
                    {
                        csentry["jobTitle"].StringValue = mventry["jobTitle"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["jobTitle"].Delete();
                    }
                    break;
                case "cd.Identity:firstName<-mv.person:firstName":
                    if (mventry["firstName"].IsPresent)
                    {
                        csentry["firstName"].StringValue = mventry["firstName"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["firstName"].Delete();
                    }
                    break;
                case "cd.Identity:lastName<-mv.person:lastName":
                    if (mventry["lastName"].IsPresent)
                    {
                        csentry["lastName"].StringValue = mventry["lastName"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["lastName"].Delete();
                    }
                    break;


                case "cd.Identity:techName<-mv.person:accountName":
                    if (mventry["accountName"].IsPresent)
                    {
                        csentry["techName"].StringValue = mventry["accountName"].StringValue.Trim();
                    }
                    else
                    {
                        csentry["techName"].Delete();
                    }
                    break;

                // ----------------------------------------------------------------
                // GIS: aktualizace oprávnění při změně záznamu (trigger: DateChanged)
                // Pravidlo nastavte v GIS MA jako:
                //   FlowRuleName = "cd.User:permissions<-mv.person:DateChanged"
                //   Source attribute: DateChanged (nebo libovolný trigger atribut)
                //   Destination attribute: permissions
                // ----------------------------------------------------------------
                case "cd.User:permissions<-mv.person:DateChanged":
                    if (!mventry["AccountName"].IsPresent) break;

                    string gisUser  = mventry["AccountName"].StringValue.Trim();
                    string gisPerm  = Mms_Metaverse.MVExtensionObject.GetGISPermissions(gisUser);

                    if (!string.IsNullOrEmpty(gisPerm))
                    {
                        // Nastavíme aktuální oprávnění – GIS API nahradí celý seznam
                        csentry["permissions"].StringValue = gisPerm;
                    }
                    else
                    {
                        // Žádná oprávnění v iDM → prázdný řetězec = GIS API uživatele zablokuje
                        csentry["permissions"].StringValue = string.Empty;
                    }
                    break;

                default:
                    throw new EntryPointNotImplementedException();
            }
        }
    }
}
