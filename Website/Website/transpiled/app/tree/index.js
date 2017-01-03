"use strict";
var ngmodule_1 = require("../bootstrap/ngmodule");
var tree_component_1 = require("./tree.component");
var tree_states_1 = require("./tree.states");

var globalAppModule = {
    components: { tree: tree_component_1.tree },
    states: [tree_states_1.treeState],
};
ngmodule_1.loadNg1Module(ngmodule_1.ngmodule, globalAppModule);
//# sourceMappingURL=index.js.map