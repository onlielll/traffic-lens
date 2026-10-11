# 第一次上传 GitHub

源码放在仓库里，可运行安装包和便携包放在 Releases 里。下面以 `traffic-lens` 为例，说明首次上传和后续更新。

## 1. 上传整理后的源码

1. 解压 `TrafficLens-2.0.0-source.zip`，得到 `GitHub-Source` 文件夹。
2. 安装 GitHub Desktop，登录自己的 GitHub 账号。
3. 如果已在 GitHub 创建空仓库，先用 **File → Clone repository** 克隆它；否则用 **File → New repository** 新建名为 `traffic-lens` 的本地仓库。
4. 将 `GitHub-Source` **里面的全部内容**复制到新仓库目录，包括 `.github`、`.gitignore` 等以点开头的文件；不要再套一层 `GitHub-Source`。
5. 在 Changes 中检查文件，填写 `Initial public preview` 并提交。
6. 已克隆的仓库点击 **Push origin**。新建本地仓库点击 **Publish repository**，填写 [仓库介绍](REPOSITORY-INTRO.md)；取消 **Keep this code private** 将其公开，保留则先私有保存。

GitHub Desktop 的 **Local path** 是电脑里的仓库位置；**Publish repository** 才上传到 GitHub。[官方发布说明](https://docs.github.com/en/desktop/adding-and-cloning-repositories/adding-an-existing-project-to-github-using-github-desktop)

源码包已排除运行数据、调查日志、旧发布目录和 SDK。提交时仍应检查 Changes，不要额外加入代理订阅、密钥、数据库或个人流量截图。`.gitignore` 不会移除已经提交的文件。

仓库已采用 MIT 许可证，允许修改、分发和商用，要求保留许可与版权说明。随包组件和安装器条款见 [第三方说明](../THIRD-PARTY-NOTICES.md)。

## 2. 提供软件下载

仓库页面打开 **Releases → Draft a new release**：

1. 创建 `v2.0.0` tag，选择包含此次源码的分支。
2. 标题填写 `Traffic Lens v2.0.0`，说明使用 [准备好的首次发布介绍](REPOSITORY-INTRO.md)。
3. 添加三个架构的 `-setup.exe`、`-portable.zip`，以及 `TrafficLens-2.0.0-source.zip` 和 `SHA256SUMS.txt`。
4. 勾选 **This is a pre-release**，检查文件与版本一致后发布。

当前 x64 和 x86 已在 Windows 11 上运行，ARM64 尚未硬件实测；首次公开发布应保留预览标记。一般用户选择 x64 安装包即可。[GitHub Releases 官方指南](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)

安装包和 ZIP 放 Releases，避免把运行时大包放进 Git 历史。GitHub 对普通仓库文件有大小限制，并建议用 Releases 分发二进制。[大文件说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)

## 3. 自动构建与以后更新

仓库内的 GitHub Actions 可从 **Actions → Windows release packages → Run workflow** 手动构建。推送与源码版本一致的 `v*` tag 时会生成草稿 Release；检查附件后由你发布，不会直接公开成品。

若已经在网页创建同名 Release，自动构建的文件会留在 Actions 附件中，不覆盖你手动选择的下载包。

以后修改源码、测试、提交并推送，更新 CHANGELOG 和项目版本，再创建新 tag 和 Release。替换下载附件不代表源码已更新，两者应保持一致。
