using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.CognitiveServices.QnAMaker;
using Microsoft.Bot.Connector;

namespace Microsoft.Bot.Sample.QnABot
{
    [Serializable]
    public class RootDialog : IDialog<object>
    {
        public static string convert = "";
        
        public Task StartAsync(IDialogContext context)
        {
            context.Wait(HelloMessage);

            return Task.CompletedTask;
        }

        private async Task HelloMessage(IDialogContext context, IAwaitable<object> result)
        {
            await context.PostAsync("こんにちは！私はPoC Botです。どのようなご用件でしょうか？ ");

            await MenuMessage(context);
        }

        private async Task MenuMessage(IDialogContext context)
        {
            int i;

            string[] array;
            array = new string[2] { "社内手続きに関する問い合わせ", "終了" };

            string resultMessage = "以下から選択してください(番号で入力)\n";

            for (i = 0; i < 2; i++)
            {
                resultMessage = resultMessage + (i+1).ToString() + ". " + array[i] + "\n";
            }
            
            await context.PostAsync(resultMessage);

            context.Wait(SelectDialog);
        }

        private async Task SelectDialog(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var message = await result;
            
            convert = await ZenkakuConvert.Convert(message.Text);

            if(convert == "1")
            {
                context.Call(new FAQDialog(), QnaResumeAfterDialog);
            }
            else if(convert == "2")
            {
                await context.PostAsync("ご利用ありがとうございました。最後にアンケートをお願いできますか？");
                context.Call(new EnqueteDialog(), EnqueteResumeAfterDialog);
            }
        }

        private async Task QnaResumeAfterDialog(IDialogContext context, IAwaitable<object> result) 
        {
            await FeedbackMessage(context);
        }
        
        private async Task FeedbackMessage(IDialogContext context)
        {
            int i;

            string[] array;
            array = new string[2] { "はい", "いいえ" };

            string resultMessage = "解決しましたか？(番号で入力)\n";

            for (i = 0; i < 2; i++)
            {
                resultMessage = resultMessage + (i + 1).ToString() + ". " + array[i] + "\n";
            }

            await context.PostAsync(resultMessage);
            context.Wait(FeedbackDialog);
        }


        private async Task FeedbackDialog(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var feedbackMenu = await result;
            
            convert = await ZenkakuConvert.Convert(feedbackMenu.Text);
            
            if(convert == "1")
            {
                await context.PostAsync("ご利用ありがとうございました。");
                await MenuMessage(context);
            }
            else if(convert == "2")
            {
                await context.PostAsync("どのような回答をご希望でしたか？");
                context.Wait(InputMessage);
            }
            
        }

        private async Task InputMessage(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            await context.PostAsync("フィードバックありがとうございます。今後の精度改善の参考にさせて頂きます。");

            await MenuMessage(context);
        }

        private async Task EnqueteResumeAfterDialog(IDialogContext context, IAwaitable<string> result)
        {
            await context.PostAsync($"ご協力、ありがとうございました。");

            await MenuMessage(context); 
        }

    }

    /**********************************************************************************/
    // QnaDialog.cs
    [Serializable]
    public class FAQDialog : IDialog<object>
    {
        public static string json = "";
        public static string convert = "";

        public async Task StartAsync(IDialogContext context)
        {
            await context.PostAsync("どのようなことでお困りでしょうか？文章で質問を入力してください。");
            context.Wait(MessageReceivedAsync);
        }

        public virtual async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> item)
        {
            var message = await item;

            json = await CustomQnAMaker.GetResultAsync(message.Text);

            if (json != "failure")
            {
                var result = JsonConvert.DeserializeObject<QnAMakerResults>(json);
                await ShowQuestions(context, result);
            }
        }

        private async Task ShowQuestions(IDialogContext context, QnAMakerResults result)
        {
            int i;
            string resultMessage = "以下から選択してください(番号で入力)\n";

            for (i = 0; i < result.Answers.Count; i++)
            {
                resultMessage = resultMessage + (i+1).ToString() + ". " + result.Answers[i].Questions[0] + "\n";
            }
            resultMessage = resultMessage + (i+1).ToString() + ". 上記のどれでもない\n";
            await context.PostAsync(resultMessage);
            context.Wait(ShowAnswer);
        }

        private async Task ShowAnswer(IDialogContext context, IAwaitable<IMessageActivity> item)
        {
            var num = await item;
            var result = JsonConvert.DeserializeObject<QnAMakerResults>(json);

            convert = await ZenkakuConvert.Convert(num.Text);

            if (Int32.Parse(convert) >= 1 && Int32.Parse(convert) <= result.Answers.Count)
            {
                await context.PostAsync(result.Answers[Int32.Parse(convert) - 1].Answer.ToString());
                context.Done<object>(null);
            }
            else if (Int32.Parse(convert) == result.Answers.Count + 1)
            {
                await context.PostAsync("お役に立てず申し訳ございません。。");
                context.Done<object>(null);
            }
            else
            {
                await ShowQuestions(context, result);
            }

        }

    }
    
    /**********************************************************************************/
    // CustomQnAMaker.cs
    public class CustomQnAMaker
    {
        public static async Task<string> GetResultAsync(string messageText)
        {
            string endpoint = ConfigurationManager.AppSettings["QnAEndpointHostName"] + "/knowledgebases/" + ConfigurationManager.AppSettings["QnAKnowledgebaseId"] + "/generateAnswer";
            string input_json = "{\"question\":\"" + messageText + "\",\"top\": \"5\"}";

            using (var client = new HttpClient())
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("EndpointKey", ConfigurationManager.AppSettings["QnAAuthKey"]);
                    request.Content = new StringContent(input_json, Encoding.UTF8, "application/json");

                    using (var response = await client.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            return json;
                        }
                        string failure = "failure";
                        return failure;
                    }
                }
            }
        }
    }
    /**********************************************************************************/
    // ZenkakuConvert.cs
    public class ZenkakuConvert
    {
        public static Dictionary<char, char> dictionary =
            new Dictionary<char, char>()
            {
                {'０','0'},{'１','1'},{'２','2'},{'３','3'},
                {'４','4'},{'５','5'},{'６','6'},{'７','7'},
                {'８','8'},{'９','9'},
            };

        public static async Task<string> Convert(string source)
        {
            Regex regex = new Regex("[０-９]+");
            return regex.Replace(source, Replacer);
        }

        public static string Replacer(Match m)
        {
            return new string(m.Value.Select(n => dictionary[n]).ToArray());
        }
    }
    
    
    /**********************************************************************************/
    // EnqueteDialog.cs
    [Serializable]
    public class EnqueteDialog : IDialog<string>
    {

        public async Task StartAsync(IDialogContext context)
        {
            int i;

            string[] array;
            array = new string[5] { "大満足", "満足", "普通", "不満", "とても不満" };

            string resultMessage = "以下から選択してください(番号で入力)\n";

            for (i = 0; i < 5; i++)
            {
                resultMessage = resultMessage + (i + 1).ToString() + ". " + array[i] + "\n";
            }

            await context.PostAsync(resultMessage);

            context.Wait(SelectDialog);

        }

        private async Task SelectDialog(IDialogContext context, IAwaitable<object> result)
        {
            var selectedMenu = await result;
            context.Done(selectedMenu);
        }
    }
    /**********************************************************************************/   
       
}