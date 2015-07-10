// view model for application sample 1

var ViewModel = function () {
    var self = this;
    self.suppliers = ko.observableArray();
    self.parts = ko.observableArray();
    self.supplies = ko.observableArray();

    self.tempsupplier = ko.observable();
    self.partspattern = ko.observable({ PNAME: "S.*" });
    self.editmode = ko.observable();
    self.error = ko.observable();

    var supplierUri = '/rest/supplier';
    var partUri = '/rest/part';
    var suppliesUri = '/rest/supplies';

    // Common code to call uri with method passing JSON data or nothing
    function ajaxHelper(uri, method, id, data, raw) {
        self.error(''); // Clear error message
        return $.ajax({
            url: id ? uri + '/' + id : uri,
            type: method,
            data: raw ? data : JSON.stringify(data),
            dataType: 'json',
            contentType: 'application/json',
        }).fail(function (jqXHR, textStatus, errorThrown) {
            self.error(errorThrown);
        });
    }

    // data set specific REST functions
    self.getSuppliers = function() {
        ajaxHelper(supplierUri, 'GET').done(function (data) {
            self.suppliers(data);
        });
    }

    self.getSupplier = function(id) {
        ajaxHelper(supplierUri, 'GET', id).done(function (data) {
            self.suppliers(data);
        });
    }

    self.updateSupplier = function(id) {
        ajaxHelper(supplierUri, 'PUT', id, [ ko.toJS(self.tempsupplier) ] )
            .done(function () {
                //alert("OK");
                self.cancelSupplier();
                self.getSuppliers();
            });
    }

    self.addSupplier = function() {
        ajaxHelper(supplierUri, 'POST', null,  [ ko.toJS(self.tempsupplier) ] )
            .done(function () {
                //alert("OK");
                self.cancelSupplier();
                self.getSuppliers();
        });
    }

    self.deleteSupplier = function(rec) {
        ajaxHelper(supplierUri, 'DELETE', rec.Sid).done(function () {
            //alert("OK");
            self.getSuppliers();
        });
    }

    // other data sets

    self.getParts = function() {
        ajaxHelper(partUri, 'GET').done(function (data) {
            self.parts(data);
        });
    }

    self.getSupplies = function() {
        ajaxHelper(suppliesUri, 'GET').done(function (data) {
            self.supplies(data);
        });
    }

    self.findParts = function () {
        var arg = ko.toJS(self.partspattern);
        ajaxHelper(partUri, 'GET', null, arg, true).done(function (data) {
            self.parts(data);
        });
    };

    // CRUD glue

    self.newSupplier = function () {
        self.editmode('new');
    };

    self.cancelSupplier = function () {
        self.editmode(false);
        self.tempsupplier({
            Sid: "",
            SNAME: "",
            STATUS: 0,
            CITY: ""
        })
    };

    self.getSuppliers();
    self.getSupplies();
    self.getParts();
    self.cancelSupplier();
    //getSbyname( 'a' );
};

ko.applyBindings(new ViewModel());
