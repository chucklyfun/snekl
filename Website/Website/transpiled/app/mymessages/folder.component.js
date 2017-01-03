"use strict";
var ngmodule_1 = require("../bootstrap/ngmodule");
require("./directives/messageTable");
exports.folderComponent = "folder";
ngmodule_1.ngmodule.component(exports.folderComponent, {
    bindings: { folder: '<', messages: '<' },
    template: "\n<div class=\"messages\">\n  <message-table columns=\"$ctrl.folder.columns\" messages=\"$ctrl.messages\"></message-table>\n</div>\n" });
//# sourceMappingURL=folder.component.js.map