﻿using EnvDTE;
using QuickJump2022.Forms;
using QuickJump2022.Models;
using QuickJump2022.Services;

namespace QuickJump2022;

[Command(PackageIds.ShowCommandSearchForm)]
internal sealed class ShowCommandSearchForm : BaseCommand<ShowCommandSearchForm> {
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e) {
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync();
        await SearchForm.ShowModalAsync(Package as QuickJumpPackage, Enums.ESearchType.Commands);
    }
}
