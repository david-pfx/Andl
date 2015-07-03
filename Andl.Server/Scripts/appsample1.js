// view model for application sample 1

var ViewModel = function () {
    var self = this;
    self.suppliers = ko.observableArray();
    self.parts = ko.observableArray();
    self.supplies = ko.observableArray();
    self.products = ko.observableArray();
    self.sandsp = ko.observableArray();
    self.sbyname = ko.observableArray();

    self.error = ko.observable();
    self.rawjson = "??";
    
    var supplierUri = '/api/main/suppliers';
    var partUri = '/api/main/parts';
    var suppliesUri = '/api/main/supplies';
    var productsUri = '/api/main/products';
    var sandspUri = '/api/main/sandsp';
    var sbynameUri = '/api/main/sbyname';

    function ajaxHelper(uri, method, data) {
        self.error(''); // Clear error message
        return $.ajax({
            type: method,
            url: uri,
            dataType: 'json',
            contentType: 'application/json',
            data: data ? JSON.stringify(data) : null
        }).fail(function (jqXHR, textStatus, errorThrown) {
            self.error(errorThrown);
        });
    }

    function getSuppliers() {
        ajaxHelper(supplierUri, 'GET').done(function (data) {
            self.suppliers(data);
        });
    }

    function getParts() {
        ajaxHelper(partUri, 'GET').done(function (data) {
            self.parts(data);
        });
    }

    function getSupplies() {
        ajaxHelper(suppliesUri, 'GET').done(function (data) {
            self.supplies(data);
        });
    }

    function getProducts() {
        ajaxHelper(suppliesUri, 'GET').done(function (data) {
            self.products(data);
        });
    }

    function getSandSp() {
        ajaxHelper(sandspUri, 'GET').done(function (data) {
            self.sandsp(data);
        });
    }

    function getSbyname(arg) {
        ajaxHelper(sbynameUri, 'POST', arg).done(function (data) {
            self.sbyname(data);
        });
    }

    getSuppliers();
    getSupplies();
    getParts();
    getProducts();
    getSandSp();
    getSbyname( 'a' );
};

ko.applyBindings(new ViewModel());

$(document).ready(
    $('#sbnbuttonid').click(function() { ko.viewModel.getSbyname($('#sbntextid')); })
 );
