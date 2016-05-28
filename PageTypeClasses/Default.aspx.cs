using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.UI.WebControls;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Filters;
using EPiServer.PlugIn;
using EPiServer.Security;
using EPiServer.ServiceLocation;
using EPiServer.Shell.WebForms;
using EPiServer.SpecializedProperties;
using EPiServer.Web;

namespace Web.Plugins
{
    [GuiPlugIn(Area = PlugInArea.AdminMenu, DisplayName = "Create Classes from Page Types",
        RequiredAccess = AccessLevel.Administer, Url = "~/PageTypeClasses/Default.aspx")]
    public partial class CreatePageTypeClasses : WebFormsBase
    {
        private StringBuilder _changeLog;
        private StringBuilder ChangeLog
        {
            get { return _changeLog ?? (_changeLog = new StringBuilder()); }
        }

        private static string CodeIndent
        {
            get
            {
                return "\t\t";
            }
        }

        protected override void OnPreInit(EventArgs e)
        {
            base.OnPreInit(e);
            SystemMessageContainer.Heading = Title;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (IsPostBack == false)
            {
                var repository = ServiceLocator.Current.GetInstance<PageTypeRepository>();
                PageTypeCheckBoxList.DataSource = repository.List();
                PageTypeCheckBoxList.DataTextField = "Name";
                PageTypeCheckBoxList.DataValueField = "ID";
                PageTypeCheckBoxList.DataBind();
            }
        }

        protected void ButtonSubmitClick(object sender, EventArgs e)
        {
            var ret = new StringBuilder();
            Page.Validate();
            if (Page.IsValid)
            {
                PlaceHolderInput.Visible = false;
                CreatePageTypeFiles(ret);
                CreateChangeLogFile(new Dictionary<string, StringBuilder>
                                        {
                                            {"Changed properties: ", ChangeLog},
                                            {"Pagetypes info: ", ret}
                                        });
                OutputLiteral.Text = ret.ToString();
            }
        }

        private void CreateChangeLogFile(Dictionary<string, StringBuilder> stringBuildersToWrite)
        {
            var dir = OutputDirectory(string.Empty);
            using (var sw = File.CreateText(dir + "\\ChangeLog.txt"))
            {
                foreach (var stringBuilder in stringBuildersToWrite)
                {
                    sw.WriteLine("{0}{1}{2}{1}", stringBuilder.Key, Environment.NewLine, stringBuilder.Value.ToString().Replace("<br />", Environment.NewLine));
                }
            }
        }

        private void CreatePageTypeFiles(StringBuilder ret)
        {
            foreach (ListItem li in PageTypeCheckBoxList.Items)
            {
                if (li.Selected == false)
                {
                    var repository = ServiceLocator.Current.GetInstance<PageTypeRepository>();
                    var pageType = repository.Load(int.Parse(li.Value));
                    var dir = OutputDirectory(pageType.Name);
                    var className = GetClassName(pageType.Name);
                    var classCont = GetClassContainer(pageType.Name);
                    ret.AppendFormat("Name: {0}, ClassName: {1}, Directory: {2}<br />", pageType.Name,
                                     GetClassName(pageType.Name), dir);
                    if (Directory.Exists(dir) == false)
                    {
                        Directory.CreateDirectory(OutputDirectory(pageType.Name));
                    }
                    using (var sw = File.CreateText(dir + "\\" + className + ".cs"))
                    {
                        var desc = pageType.Description ?? string.Empty;
                        sw.WriteLine(UsingTextBox.Text);
                        sw.WriteLine(
                                @"
namespace {0}
{{
    [TemplateDescriptor(
        Path = ""{3}"")]
    [ContentType(
        GUID = ""{1}"",
        DisplayName = ""{2}"",
        Description = ""{5}"",
        Order = {6},
        AvailableInEditMode = {12})]{11}
    public class {8} : {9}
    {{
        {10}
        #region [ Defaults ]
        
        public override void  SetDefaultValues(ContentType contentType)
        {{
             base.SetDefaultValues(contentType);

            VisibleInMenu = {7};
            this[MetaDataProperties.PageTargetFrame] = Frame.Load({14});
            this[MetaDataProperties.PagePeerOrder] = {16};{4}{13}{15}{17}{18}
        }}

        #endregion

    }}
}}",
                            /*0*/ NamespaceTextBox.Text + (string.IsNullOrEmpty(classCont) ? string.Empty : "." + classCont),
                            /*1*/ pageType.GUID,
                            /*2*/ pageType.Name,
                            /*3*/ pageType.FileName,
                            /*4*/ pageType.Defaults.ChildOrderRule != FilterSortOrder.None ? string.Format("{1}{1}{1}this[MetaDataProperties.PageChildOrderRule] = FilterSortOrder.{0};{2}", pageType.Defaults.ChildOrderRule, CodeIndent, Environment.NewLine) : "",
                            /*5*/ desc.Replace("\"", "'"),
                            /*6*/ pageType.SortOrder,
                            /*7*/ pageType.Defaults.VisibleInMenu.ToString(CultureInfo.InvariantCulture).ToLower(),
                            /*8*/ className,
                            /*9*/ BaseClassTextBox.Text,
                            /*10*/ GetProperties(pageType),
                            /*11*/ GetAvailablePageTypes(pageType, repository),
                            /*12*/ pageType.IsAvailable.ToString(CultureInfo.InvariantCulture).ToLower(),
                            /*13*/ PageReference.IsNullOrEmpty(pageType.Defaults.ArchivePageLink) ? string.Empty : string.Format(@"{1}{1}{1}ArchiveLink = new PageReference(""{0}"");{2}", pageType.Defaults.ArchivePageLink, CodeIndent, Environment.NewLine),
                            /*14*/ pageType.Defaults.DefaultFrame.ID,
                            /*15*/ string.IsNullOrEmpty(pageType.Defaults.DefaultPageName) ? string.Empty : string.Format(@"{1}{1}{1}PageName = ""{0}""{2}", pageType.Defaults.DefaultPageName, CodeIndent, Environment.NewLine),
                            /*16*/ pageType.Defaults.PeerOrder,
                            /*17*/ pageType.Defaults.StartPublishOffset == TimeSpan.Zero ? string.Empty : string.Format(@"{1}{1}{1}StartPublish = DateTime.Now.Add(TimeSpan.Parse(""{0}""));{2}", pageType.Defaults.StartPublishOffset, CodeIndent, Environment.NewLine),
                            /*18*/ pageType.Defaults.StopPublishOffset == TimeSpan.Zero ? string.Empty : string.Format(@"{1}{1}{1}StopPublish = DateTime.Now.Add(TimeSpan.Parse(""{0}""));{2}", pageType.Defaults.StopPublishOffset, CodeIndent, Environment.NewLine));
                        sw.Flush();
                    }
                }
                else
                {
                    ret.AppendFormat("Ignoring {0}<br />", li.Text);
                }
            }
        }

        private object GetProperties(PageType pageType)
        {
            var stringBuilder = new StringBuilder();
            const int sortConst = 100;
            const int tabSectionConst = 1000;
            var sortOrder = sortConst;
            var tabSection = tabSectionConst;
            foreach (var group in pageType.PropertyDefinitions.GroupBy(d => d.Tab.Name))
            {
                stringBuilder.AppendFormat("{1}{2}#region {0}{1}{1}", group.Key, Environment.NewLine, CodeIndent);

                foreach (var definition in group.OrderBy(t => t.FieldOrder))
                {
                    var newPropertyName = definition.Name;
                    if (definition.Name.Contains("-"))
                    {
                        newPropertyName = definition.Name.Replace("-", "");
                        ChangeLog.AppendFormat(" Changed property from: '" + definition.Name + "' to: '" + newPropertyName + "'");
                    }

                    if (definition.Required)
                        stringBuilder.AppendFormat("{0}[Required]{1}", CodeIndent, Environment.NewLine);
                    if (definition.Searchable)
                        stringBuilder.AppendFormat("{0}[Searchable]{1}", CodeIndent, Environment.NewLine);
                    if (definition.LanguageSpecific)
                        stringBuilder.AppendFormat("{0}[CultureSpecific]{1}", CodeIndent, Environment.NewLine);
                    if (definition.Type.DataType == PropertyDataType.LongString && DefinitionTypeIs<PropertyXhtmlString>(definition.Type) == false && DefinitionTypeIs<PropertyContentArea>(definition.Type) == false)
                        stringBuilder.AppendFormat(@"{0}[UIHint(""textarea"")]{1}", CodeIndent, Environment.NewLine);
                    if (definition.DisplayEditUI == false)
                        stringBuilder.AppendFormat("{0}[ScaffoldColumn(false)]{1}", CodeIndent, Environment.NewLine);

                    string backingType = GetBackingType(definition);
                    if (string.IsNullOrEmpty(backingType) == false)
                        stringBuilder.AppendLine(backingType);

                    stringBuilder.AppendFormat(
        @"{0}[Display(Name = ""{1}"",
            Description = ""{2}"",
            Order = {3},
            GroupName = ""{4}"")]
        public virtual {5} {6} {{ get; set; }}",
                        /*0*/ CodeIndent,
                        /*1*/ (definition.EditCaption ?? definition.Name).Replace("\"", "'"),
                        /*2*/ (definition.HelpText ?? string.Empty).Replace("\"", "'"),
                        /*3*/ sortOrder,
                        /*4*/ definition.Tab.Name,
                        /*5*/ GetDataType(definition.Type),
                        /*6*/ newPropertyName);

                    stringBuilder.AppendLine(string.Empty);
                    stringBuilder.AppendLine(string.Empty);

                    sortOrder += 100;
                }
                stringBuilder.AppendFormat("{1}#endregion{0}", Environment.NewLine, CodeIndent);
                sortOrder = sortConst + tabSection;
                tabSection += tabSectionConst;
            }
            return stringBuilder.ToString();
        }

        private string GetBackingType(PropertyDefinition definition)
        {
            Type definitionType = definition.Type.DefinitionType;
            string uiHint = null;
            if (IsType<PropertyString>(definitionType)
                || IsType<PropertyLongString>(definitionType)
                || IsType<PropertyPageReference>(definitionType)
                || IsType<PropertyBoolean>(definitionType)
                || IsType<PropertyXhtmlString>(definitionType)
                || IsType<PropertyContentArea>(definitionType)
                || IsType<PropertyNumber>(definitionType)
                || IsType<PropertyUrl>(definitionType)
                || IsType<PropertyLinkCollection>(definitionType)
                || IsType<PropertyDate>(definitionType)
                || IsType<PropertyXForm>(definitionType)
                )
                return string.Empty;

            if (IsType<PropertyDropDownList>(definitionType))
                uiHint = @"""DropDownList""";
            if (IsType<PropertyAppSettings>(definitionType))
                uiHint = @"""AppSettings""";
            if (IsType<PropertyImageUrl>(definitionType))
                uiHint = "UIHint.Image";
            if (IsType<PropertyDocumentUrl>(definitionType))
                uiHint = "UIHint.Document";

            if (string.IsNullOrEmpty(uiHint) == false)
                return string.Format("{1}[UIHint({0})]", uiHint, CodeIndent);

            string attribute = string.Format("[BackingType(typeof({0}))]", definitionType.FullName);

            if (BackingTypeCheckBox.Checked)
            {
                return string.Format("{1}{0}", attribute, CodeIndent);
        }

            return string.Format("{2}// Implement {0} or uncomment this attribute: {1}",
                definitionType.FullName,
                attribute,
                CodeIndent);
        }

        private bool IsType<T>(Type definitionType)
        {
            return typeof(T) == definitionType;
        }

        private string GetDataType(PropertyDefinitionType definitionType)
        {
            PropertyDataType propertyDataType = definitionType.DataType;
            switch (propertyDataType)
            {
                case PropertyDataType.Boolean:
                    return "bool";
                case PropertyDataType.Category:
                    return "CategoryCollection";
                case PropertyDataType.Date:
                    return "DateTime";
                case PropertyDataType.FloatNumber:
                    return "double";
                case PropertyDataType.LinkCollection:
                    return "LinkItemCollection";
                case PropertyDataType.LongString:
                    //In my example project we had Custom Properties inheriting from PropertyXhtmlString only to programatically set available buttons
                    if (DefinitionTypeIs<PropertyXhtmlString>(definitionType))
                        return "XhtmlString";
                    if (DefinitionTypeIs<PropertyContentArea>(definitionType))
                        return "ContentArea";
                    return "string";
                case PropertyDataType.Number:
                    return "int";
                case PropertyDataType.PageReference:
                    return "PageReference";
                case PropertyDataType.ContentReference:
                    return "ContentReference";
                case PropertyDataType.PageType:
                    return "int";
                case PropertyDataType.String:
                    if (typeof(PropertyUrl).IsAssignableFrom(definitionType.DefinitionType))
                        return "Url";
                    if (typeof(PropertyXForm).IsAssignableFrom(definitionType.DefinitionType))
                        return "XForm";
                    return "string";
                default:
                    return "object";
            }
        }

        private static bool DefinitionTypeIs<TPropertyDefinitionType>(PropertyDefinitionType definitionType)
        {
            return typeof(TPropertyDefinitionType).IsAssignableFrom(definitionType.DefinitionType);
        }

        private string OutputDirectory(string name)
        {
            return Server.MapPath("/" + OutputTextBox.Text.Trim() + "/" + GetClassContainer(name));
        }

        private string GetClassContainer(string name)
        {
            const string pattern = @"\[(.*?)\]";
            var match = Regex.Match(name, pattern);
            return match.Value.TrimStart('[').TrimEnd(']');
        }

        private string GetClassName(string name)
        {
            const string pattern = @"\[(.*?)\]";
            name = Regex.Replace(name, pattern, "");
            name = ReplaceIllegalChars(name);
            return PrefixTextBox.Text.Trim() + name + SuffixTextBox.Text.Trim();
        }

        private string ReplaceIllegalChars(string inputString)
        {
            var regexFindInvalidUrlChars = new Regex(@"[^A-Za-z0-9\-_~]{1}", RegexOptions.Compiled);
            var urlCharacterMap = UrlSegment.GetURLCharacterMap();

            var builder = new StringBuilder(inputString);
            var matchs = regexFindInvalidUrlChars.Matches(inputString);
            for (var i = 0; i < matchs.Count; i++)
            {
                var obj2 = urlCharacterMap[builder[matchs[i].Index]];
                if (obj2 != null)
                {
                    builder[matchs[i].Index] = (char)obj2;
                }
                else
                {
                    builder[matchs[i].Index] = '?';
                }
            }
            builder.Replace("?", "");
            builder.Replace(" ", "");
            builder.Replace("-", "");
            return builder.ToString();
        }

        private string GetAvailablePageTypes(PageType pageType, PageTypeRepository repository)
        {
            // If you're not on EPiServer CMS 8, uncomment these lines (leave the last return)
            
            var allowedPageTypeNames = ServiceLocator.Current.GetInstance<ContentTypeAvailabilityService>().GetSetting(pageType.Name).AllowedContentTypeNames
                                                                                                                          .Select(name =>
                                                                                                                              {
                                                                                                                                  var container = GetClassContainer(name);
                                                                                                                                  string className = string.IsNullOrEmpty(container) ? GetClassName(name) : container + "." + GetClassName(name);
                                                                                                                                  return string.Format("typeof({0})", className);
                                                                                                                              })
                                                                                                                          .ToList();
            if (allowedPageTypeNames.Any())
            {
                return string.Format(@"{0}[AvailableContentTypes(Include = new[] {{ {1} }})]", Environment.NewLine, string.Join(",", allowedPageTypeNames));
            }
            return string.Empty;
        }
    }
}
