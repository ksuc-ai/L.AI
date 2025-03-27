using Community.VisualStudio.Toolkit;
using L_AI.Commands.ToolbarHelper;
using L_AI.Options;
using L_AI.TextGeneration;
using L_AI.TextGeneration.WebApi;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace L_AI.CodeGeneration
{
    public class GenerationSource
    {
        private static GenerationSource _instance = null;
        public static GenerationSource Instance => _instance ?? (_instance = new GenerationSource());

        public bool IsBusy { get; private set; }

        private GenerationSource()
        {
            GenerationManager.ConnectivityChanged += ConnectivityChanged;
        }

        public void Init()
        {
            // Empty on purpose
        }

        private void ConnectivityChanged(object sender, ConnectivityEventArgs eventArgs)
        {
            CheckConnectionAndDisplayInfoAsync().Forget();
        }

        public async Task<string> GetSuggestionAsync(string code, int position, CancellationToken cancellationToken, bool singleline = false, IEnumerable<string> extraCode = null)
        {
            IsBusy = true;

            var settings = GeneralOptions.Instance;

            if (GenerationManager.IsConnected)
                await MenuStatusHandler.SetIconColorAsync(MenuStatusHandler.IconStatusColor.Blue);

            if (settings.LimitAutocompleteContext)
            {
               TrimSentContext(ref code, ref position, settings.LimitedContextLength);
            }

            var prompt = BuildPrompt(code, position, extraCode);
            var stopStrings = BuildStopStrings(singleline);
            var model = new GenerationRequestModel
            {
                Prompt = prompt,
                Stop = stopStrings,
            };

            var generationRequest = await GenerationManager.RequestAutocomplete(model, cancellationToken);

            IsBusy = false;

            if (cancellationToken.IsCancellationRequested)
            {
                await GenerationManager.RequestCancellation();
                return null;
            }

            if (!generationRequest.IsSuccessful)
                return null;

            await MenuStatusHandler.SetIconColorAsync(MenuStatusHandler.IconStatusColor.Green);
            return generationRequest.Data;
        }

        private void TrimSentContext(ref string code, ref int position, int contextLength)
        {
            int codeLength = code.Length;

            if (codeLength <= contextLength)
                return;

            int codePostfixLength = codeLength-position;
            int middle = contextLength/2;

            if(middle>position)
            {
                // Context overflows at the beginning of file
                code = code.Substring(0, contextLength);
                return;
            }
            else if(middle>codePostfixLength)
            {
                // Context overflows at the end of file
                code = code.Substring(codeLength - contextLength);
                position = contextLength-codePostfixLength;
                return;
            }

            // Context does not overflow
            code = code.Substring(position - middle, contextLength);
            position = middle;
        }

        private string BuildPrompt(string code, int position, IEnumerable<string> extraCode)
        {
            var settings = GeneralOptions.Instance;
            var sb = new StringBuilder();
            sb.Append(settings.StartToken);
            if (extraCode != null)
                foreach (var text in extraCode)
                {
                    sb.Append(text);
                    sb.Append("\r\n");
                }

            code = code.Insert(position, settings.HoleToken);
            sb.Append(code);
            sb.Append(settings.EndToken);
            return sb.ToString();
        }
        
        private string[] BuildStopStrings(bool singleline)
        {
            var settings = GeneralOptions.Instance;

            var stopSeq = new List<string>(settings.StopSequence);
            if (singleline)
                stopSeq.AddRange(new[] { "\r", "\n", "\r\n" });

            return stopSeq.ToArray();
        }

        public async Task CheckConnectionAndDisplayInfoAsync()
        {
            if (_disableMessages) return;
            await MenuStatusHandler.SetIconColorAsync(MenuStatusHandler.IconStatusColor.Yellow);

            //InfoBarModel infobarModel = null;

            var status = GenerationManager.IsConnected;
            if (status)
            {
                //infobarModel = new InfoBarModel(
                //    new[] {
                //        new InfoBarTextSpan($"L.AI: Connected to {GeneralOptions.Instance.ApiBaseEndpoint}\nAI Model: {GenerationManager.Model}\n\n"),
                //        new InfoBarHyperlink("Disable messages for this session"),
                //    }, KnownMonikers.StatusOK, true);

                await MenuStatusHandler.SetIconColorAsync(MenuStatusHandler.IconStatusColor.Green);
            }

            if (!status)
            {
                //infobarModel = new InfoBarModel(
                //    new[] {
                //        new InfoBarTextSpan($"L.AI: API is unreachable ({GeneralOptions.Instance.ApiBaseEndpoint}). Please, check settings in Tools -> Options -> L.AI\n\n"),
                //        new InfoBarHyperlink("Disable messages for this session"),
                //    }, KnownMonikers.StatusError, true);

                await MenuStatusHandler.SetIconColorAsync(MenuStatusHandler.IconStatusColor.Red);
            }

            //InfoBar infoBar = await VS.InfoBar.CreateAsync(infobarModel);

            //// Yes, possible, since can start updating before UI inited
            //if(infoBar != null)
            //{
            //    infoBar.ActionItemClicked += InfoBar_ActionItemClicked;
            //    await infoBar.TryShowInfoBarUIAsync();
            //}
        }

        private bool _disableMessages;
        private void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.Text == "Disable messages for this session")
            {
                _disableMessages = true;
                (sender as InfoBar).Close();
            }
        }
    }
}
