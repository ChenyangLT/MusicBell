# ClassIsland 联动

两种用法可以同时存在，互不冲突：

- **软件内排时间**：MusicBell 自己到点播放，ClassIsland 开不开都行。
- **ClassIsland 触发**：课表里加「行动」，由 ClassIsland 在事件（上课、下课、定时……）时叫 MusicBell 放歌。

## 一、把插件装进 ClassIsland

插件 id 是 `chenyanglt.musicbell`，源码在 [MusicBell.ClassIsland](https://github.com/ChenyangLT/MusicBell.ClassIsland)。

**方式 A（推荐）**
1. 打开 MusicBell →「设置 → ClassIsland 联动」→ 点「释放插件」，得到 `MusicBell.ClassIsland.cipx`；
2. 打开 ClassIsland →「设置 → 插件」→ 从本地安装 → 选这个 `.cipx`；
3. 重启 ClassIsland 或在插件页确认它显示「已启用」。

**方式 B（手动拷贝）**
把释放出来的 `MusicBell.ClassIsland` 文件夹整个复制到：

~~~text
ClassIsland\data\Plugins\MusicBell.ClassIsland\
~~~

里面应该有 `manifest.yml`、`MusicBell.ClassIsland.dll`、`icon.png`、`README.md`。

> 看到「该插件需要 ClassIsland 2.1.x」是正常的：插件就是按 2.1 编的。

## 二、在「自动化」里用

1. ClassIsland →「设置 → 自动化」→ 先把最上面的**「启用自动化」总开关打开**（默认是关的，这是最容易踩的坑）。
2. 「添加自动化」→ 加一个触发（例如「上课」「下课」「定时」「当前时间状态变化」）。
3. 在「触发行动」里点「添加行动」→ 选分组「音乐铃」下的：
   - **MusicBell 放一首歌**：填「歌名」（会原样交给网易云搜索框，建议写「歌手 歌名」）、可填「显示名」、可填 MusicBell.exe 的完整路径；
   - **MusicBell 放下一首排歌**：让 MusicBell 立刻播放它自己排的下一首；
   - **MusicBell 播放 / 暂停**：切内置播放器的暂停状态；
   - **MusicBell 停止播放**：停掉正在放的内置播放器；
   - **MusicBell 打开网易云音乐**：把客户端叫到前台（没开就启动）；
   - **MusicBell 关闭网易云音乐**：直接结束 cloudmusic.exe，适合「放学」「午休结束」这类收尾。
4. 保存（不需要点「保存」，设置是即时写入的），再打开自动化列表确认它是启用的。

## 三、协议参数

插件本质是调起自定义协议，你也可以在别处直接用：

~~~text
musicbell://play?q=周杰伦%20晴天&label=早读铃&id=186016&src=classisland
musicbell://next?src=classisland
musicbell://pause
musicbell://stop
musicbell://close
musicbell://open
~~~

| 参数 | 说明 |
| --- | --- |
| `q` | 歌名（必填），也是网易云搜索框里会出现的文字 |
| `label` | 呼出提示里显示的名字，留空就用 `q` |
| `id` | 网易云歌曲 ID（可选；带上它时内置播放器可以直接播这一首，不用绕搜索） |
| `src` | 来源标记，写日志用 |

| 协议 | 作用 |
| --- | --- |
| `musicbell://play` | 放一首歌 |
| `musicbell://next` | 放「下一个排歌」 |
| `musicbell://pause` | 内置播放器播放 / 暂停 |
| `musicbell://stop` | 停止内置播放器 |
| `musicbell://open` | 打开（或前置）网易云客户端 |
| `musicbell://close` | 关掉网易云客户端 |

命令行等价写法：

~~~text
MusicBell.exe --play "周杰伦 晴天" --label "早读铃"
MusicBell.exe --play-next
MusicBell.exe --pause
MusicBell.exe --stop
MusicBell.exe --close
MusicBell.exe --open-native
~~~

## 四、注意

- 协议注册指向当前 exe；换了安装位置后，在「设置 → ClassIsland 联动」里点一次「注册协议」即可。
- ClassIsland 里「外部逻辑路径」留空时，插件会依次找 `%LocalAppData%\MusicBell\MusicBell.exe`、`Program Files\MusicBell\MusicBell.exe` 和它自己所在目录，还找不到就报错，这时把路径填上。
- MusicBell 必须处于运行状态（托盘里）才能被叫醒；关掉「启用协议响应」会让这一路失效，但软件内的排歌不受影响。
