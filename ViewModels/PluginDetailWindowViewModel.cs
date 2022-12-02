using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX.Web.Rules;
using KitX_Dashboard.Commands;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels
{
    internal class PluginDetailWindowViewModel : ViewModelBase
    {
        public PluginDetailWindowViewModel()
        {
            InitCommands();
        }

        internal void InitCommands()
        {
            FinishCommand = new(Finish);
        }

        internal PluginStruct? PluginDetail { get; set; }

        internal string? DisplayName
        {
            get
            {
                if (PluginDetail != null)
                {
                    string key = Program.Config.App.AppLanguage;
                    bool? exist = PluginDetail?.DisplayName.ContainsKey(key);
                    if (exist != null && (bool)exist)
                        return PluginDetail?.DisplayName[key];
                    else return PluginDetail?.Name;
                }
                else return PluginDetail?.Name;
            }
        }

        internal string? Version => PluginDetail?.Version;

        internal string? AuthorName => PluginDetail?.AuthorName;

        internal string? PublisherName => PluginDetail?.PublisherName;

        internal string? AuthorLink => PluginDetail?.AuthorLink;

        internal string? PublisherLink => PluginDetail?.PublisherLink;

        internal string? SimpleDescription
        {
            get
            {
                if (PluginDetail != null)
                {
                    string key = Program.Config.App.AppLanguage;
                    bool? exist = PluginDetail?.SimpleDescription.ContainsKey(key);
                    if (exist != null && (bool)exist)
                        return PluginDetail?.SimpleDescription[key];
                    else return PluginDetail?.SimpleDescription.Values.ToArray()[0];
                }
                else return PluginDetail?.SimpleDescription.Values.ToArray()[0];
            }
        }

        internal string? ComplexDescription
        {
            get
            {
                if (PluginDetail != null)
                {
                    string key = Program.Config.App.AppLanguage;
                    bool? exist = PluginDetail?.ComplexDescription.ContainsKey(key);
                    if (exist != null && (bool)exist)
                        return PluginDetail?.ComplexDescription[key];
                    else return PluginDetail?.ComplexDescription.Values.ToArray()[0];
                }
                else return PluginDetail?.ComplexDescription.Values.ToArray()[0];
            }
        }

        internal string? TotalDescriptionInMarkdown
        {
            get
            {
                if (PluginDetail != null)
                {
                    string key = Program.Config.App.AppLanguage;
                    bool? exist = PluginDetail?.TotalDescriptionInMarkdown.ContainsKey(key);
                    if (exist != null && (bool)exist)
                        return PluginDetail?.TotalDescriptionInMarkdown[key];
                    else return PluginDetail?.TotalDescriptionInMarkdown.Values.ToArray()[0];
                }
                else return PluginDetail?.TotalDescriptionInMarkdown.Values.ToArray()[0];
            }
        }

        internal string? PublishDate => PluginDetail?.PublishDate.ToString("yyyy.MM.dd");

        internal string? LastUpdateDate => PluginDetail?.LastUpdateDate.ToString("yyyy.MM.dd");

        private readonly ObservableCollection<string> functions = new();

        private readonly ObservableCollection<string> tags = new();

        internal Bitmap IconDisplay
        {
            get
            {
                try
                {
                    if (PluginDetail != null)
                    {
                        byte[] src = Convert.FromBase64String(PluginDetail.Value.IconInBase64);
                        using var ms = new MemoryStream(src);
                        return new(ms);
                    }
                    else return App.DefaultIcon;
                }
                catch (Exception e)
                {
                    Log.Warning($"Icon transform error from base64 to byte[] or " +
                        $"create bitmap from MemoryStream error: {e.Message}");
                    return App.DefaultIcon;
                }
            }
        }

        internal void InitFunctionsAndTags()
        {
            if (PluginDetail != null && PluginDetail?.Functions != null && PluginDetail?.Tags != null)
            {
                string langKey = Program.Config.App.AppLanguage;
                foreach (var func in PluginDetail.Value.Functions)
                {
                    StringBuilder sb = new();
                    sb.Append(func.ReturnValueType);
                    sb.Append(" ");
                    if (func.DisplayNames.ContainsKey(langKey))
                        sb.Append(func.DisplayNames[langKey]);
                    else sb.Append(func.Name);
                    sb.Append("(");
                    if (func.Parameters.Count != func.ParametersType.Count)
                        throw new InvalidDataException("Parameters return type count " +
                            "didn't match parameters count.");
                    int index = 0;
                    foreach (var param in func.Parameters)
                    {
                        sb.Append(func.ParametersType[index]);
                        ++index;
                        sb.Append(" ");
                        if (param.Value.ContainsKey(langKey))
                            sb.Append(param.Value[langKey]);
                        else sb.Append(param.Key);
                        if (index != func.Parameters.Count)
                            sb.Append(", ");
                    }
                    sb.Append(")");
                    Functions.Add(sb.ToString());
                }
                foreach (var tag in PluginDetail.Value.Tags)
                    Tags.Add($"{{{tag.Key}: {tag.Value}}}");
            }
        }

        internal ObservableCollection<string> Functions => functions;

        internal ObservableCollection<string> Tags => tags;

        internal DelegateCommand? FinishCommand { get; set; }

        internal void Finish(object parent)
        {
            (parent as Window)?.Close();
        }
    }
}
