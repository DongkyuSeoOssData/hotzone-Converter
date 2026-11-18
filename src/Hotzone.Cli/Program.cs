using System.Text;
using Hotzone.Cli;

// 한글 파일 이름이 콘솔·파이프에서 깨지지 않게
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return await CliApp.RunAsync(args, Console.Out, Console.Error);
