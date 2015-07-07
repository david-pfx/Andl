// view model for application sample 1

var ViewModel = function () {
    var self = this;
    self.suppliers = ko.observableArray();
    self.newsuppliers = ko.observableArray();
    self.parts = ko.observableArray();
    self.supplies = ko.observableArray();
    self.sbyname = ko.observableArray();

    self.error = ko.observable();
    
    var supplierUri = '/api/main/suppliers';
    var partUri = '/api/main/parts';
    var suppliesUri = '/api/main/supplies';
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

    self.getSbyname = function (arg) {
        ajaxHelper(sbynameUri, 'POST', arg).done(function (data) {
            self.sbyname(data);
        });
    };

    self.newSupplier = function () {
        self.newsuppliers.push({
            Sid: "",
            SNAME: "",
            STATUS: "",
            CITY: ""
        })
    };

    self.addSuppliers = function () {
        //var formData = JSON.stringify($("#nsuppid").serializeArray());
        var formData = ko.toJSON($("#nsuppid").serializeArray());
        var formData = ko.toJSON($("#nsuppid"));
        ajaxHelper('api/main/addsuppliers', 'PUT', formData).done(
          function (data) {
              self.newsuppliers.removeAll();
              self.getSuppliers();
          });
    };

self.delSupplier = function (supplier) {
    self.suppliers.remove(supplier);
    };

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

    getSuppliers();
    getSupplies();
    getParts();
        //getSbyname( 'a' );
};

ko.applyBindings(new ViewModel());
