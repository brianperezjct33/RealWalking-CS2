# Real Walking — 编译与发布

**项目说明：** [README.md](README.md) · **研发记录：** [docs/DEVELOPMENT_HISTORY.md](docs/DEVELOPMENT_HISTORY.md)

Windows 本地构建：**关闭游戏后运行 `Build.cmd`**，输入 `BUILD`。构建程序自动检查环境、备份旧版、编译并部署到本地 Mods 文件夹，不会上传 Paradox Mods。需要回退时运行 **`Restore.cmd`**，输入 `RESTORE`。

源码检查由 `tools/` 中的三个 Python 脚本执行，GitHub Actions 也会自动运行。完整说明见 [BUILDING.md](BUILDING.md)。

发布请更新现有 Paradox Mods 条目 **165310**，不要创建第二个 Mod。具体操作见 [docs/PUBLISHING.md](docs/PUBLISHING.md)。
