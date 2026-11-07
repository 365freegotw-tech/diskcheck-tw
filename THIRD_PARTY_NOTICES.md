# 第三方軟體與授權

發布的程式使用以下元件；授權以各專案提供的原文為準。

| 元件 | 用途 | 授權 |
| --- | --- | --- |
| .NET 10 Runtime | 免安裝版內含執行環境 | MIT |
| Windows Presentation Foundation（WPF） | Windows 介面 | MIT |

App 專案沒有直接引用第三方 NuGet 套件。測試專案直接引用的 NuGet 套件如下（版本與授權依本機 NuGet `.nuspec` 核對）：

| 套件 | 版本 | 授權 |
| --- | --- | --- |
| coverlet.collector | 6.0.4 | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |

## CrystalDiskInfo

DiskCheck TW 0.1.2 使用 CrystalDiskInfo 的 SMART 屬性名稱表（翻譯成 `src/DiskCheckTW.Core/SmartNames.tsv` 嵌入資源），並參照 `AtaSmart.cpp` 的廠牌辨識與 SSD 壽命判斷邏輯重寫成 C#。來源為 [hiyohiyo/CrystalDiskInfo](https://github.com/hiyohiyo/CrystalDiskInfo)，commit `99f885efb57bd98ccce8c2c24188f93c81383cec`；語系原始路徑為 `Language/English.lang`、`Language/Traditional Chinese.lang`（該版本並無名為 `ChineseTraditional.lang` 的檔案）。來源授權原文如下：

```text
MIT License

Copyright (c) 2008-2023 hiyohiyo

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

授權原文：[.NET Runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)、[MIT](https://opensource.org/licenses/MIT)、[Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0)。
