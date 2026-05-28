
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
            //
            // TODO: write initialization code
            //
        }

        void IMASynchronization.Terminate()
        {
            //
            // TODO: write termination code
            //
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
                            csentry["validFrom"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
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
                            csentry["validTo"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
                    }
                    else
                    {
                        csentry["validTo"].Delete();
                    }

                    break;
                case "cd.Identity:inFrom<-mv.person:inFrom":
                    if (mventry["inFrom"].IsPresent)
                    {
                        string[] items = mventry["inFrom"].StringValue.Split('.');
                        if (items.Length == 3)
                            csentry["inFrom"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
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
                            csentry["outFrom"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
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
                            csentry["validZFrom"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
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
                            csentry["validZTo"].StringValue = $"{items[2]}-{items[1]}-{items[0]}T00:00:00";
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

                default:
                    throw new EntryPointNotImplementedException();
            }
        }
    }
}
