System.config({
  transpiler: false,
  defaultJSExtensions: true,

  packages: {
    "app": { "defaultExtension": "js" }
  },

  paths: { "npm:*": "node_modules/*", "bower:*": "bower_components/*" },

  meta: {
    "npm:angular/angular.min": { format: "global", exports: "angular" }
  },

  map: {
    "angular": "npm:angular/angular.min",
    "angular-ui-router": "npm:angular-ui-router/release/angular-ui-router",
    "angular-treeview": "bower:angular.treeview/angular.treeview",
    "bootstrap": "npm:bootstrap/dist",
    "css": "npm:systemjs-plugin-css/css",
    "d3": "npm:d3/d3.min",
    "font-awesome": "npm:font-awesome",
    "ui-router-visualizer": "npm:ui-router-visualizer/release/visualizer.min",
  }
});
