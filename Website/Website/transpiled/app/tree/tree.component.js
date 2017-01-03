"use strict";

/**
 * The controller for the Message component
 */
var TreeController = (function () {
    function TreeController($http, $scope) {
        this.$scope = $scope;

        this.$http = $http;

        this.$scope.treeView = "treeView";
        this.$scope.error = 'RRRRRRRRRRRRR';
    }
    
    TreeController.prototype.$onInit = function () {
        var vm = this.$scope;

        vm.treeView = '{treeView 2}';
        vm.error = 'ppppppppppp';

        

        this.$http.get('/post/tree/1/0/100')
            .then(function (response) {
                vm.treeView = response.data.Result;
                vm.message = "response";
                vm.$apply();
            }, function (response) {
                vm.error = response.data.ResponseStatus.Message;
                vm.$apply();
            });
    };

    return TreeController;
}());

/**
 * This component renders a single message
 *
 * Buttons perform actions related to the message.
 * Buttons are shown/hidden based on the folder's context.
 * For instance, a "draft" message can be edited, but can't be replied to.
 */
exports.tree = {
    controller: TreeController,
    // template: '<div class="ng-scope"> \n <h1>Errors</h1>\n {{error}}\n <h1>Tree View</h1>\n{{treeView}}\n </div>',
    
    templateUrl: 'partials/partial1.html'
};