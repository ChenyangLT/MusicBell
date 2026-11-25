# MusicBell 点歌铃 · ClassIsland 插件

给 ClassIsland 增加两个「行动」：

| 行动 | 做什么 |
| --- | --- |
| **MusicBell 放一首歌** | 填上「歌手 歌名」，到点时由 MusicBell 打开网易云、在搜索框里搜出来播放。 |
| **MusicBell 放下一首排歌** | 不填歌名，让 MusicBell 按自己的排歌表放下一首。 |

## 安装

1. 打开 MusicBell →「设置 → 联动」→「释放插件文件」，会得到一个 `MusicBell.ClassIsland` 文件夹和一个 `.cipx` 包。
2. ClassIsland →「设置 → 插件」→ 从本地安装，选那个 `.cipx`；或者把整个文件夹拷进
   `<ClassIsland 目录>\data\Plugins\musicbell.classisland\`。
3. 重启 ClassIsland，「设置 → 自动化 → 行动」里就能选到「音乐铃」分组。

## 用法

在「自动化」里新建一个自动化：触发条件选「时间点」或「Cron」，
行动选「MusicBell 放一首歌」，填好歌名即可。

插件只负责把歌名交给 MusicBell；真正的开网易云、搜索、播放都在 MusicBell 里做，
所以 MusicBell 需要处于运行状态（托盘里静默待着就行），或者已注册 `musicbell://` 协议让插件直接拉起它。
