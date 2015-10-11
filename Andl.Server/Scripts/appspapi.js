// view model for Andl Web REST application sample
// Native API using data model created by WebSpApiSetup.andl

var ViewModel = function () {
    var self = this;
    self.suppliers = ko.observableArray();
    self.parts = ko.observableArray();
    self.supplies = ko.observableArray();

    self.tempsupplier = ko.observable();
    self.partspattern = ko.observable({ PNAME: "S.*" });
    self.editmode = ko.observable();
    self.error = ko.observable();

    var baseUri = '/api/spapi/';

    function ajaxHelper(uri, method, data) {
        self.error(''); // Clear error message
        return $.ajax({
            type: method,
            url: uri,
            dataType: 'json',
            contentType: 'application/json',
            data: data ? JSON.stringify(data) : null
        }).fail(function (jqXHR, textStatus, errorThrown) {
            self.error(errorThrown + ' : ' + jqXHR.responseText);
        });
    }

    // data set specific API functions
    self.getSuppliers = function() {
        ajaxHelper(baseUri + 'findall_supplier', 'POST').done(function (data) {
            self.suppliers(data);
        });
    }

    self.getSupplier = function(id) {
        ajaxHelper(baseUri + 'find_supplier', 'POST', id).done(function (data) {
            self.suppliers(data);
        });
    }

    self.updateSupplier = function(id) {
        ajaxHelper(baseUri + 'update_supplier', 'POST', id, [ko.toJS(self.tempsupplier)])
            .done(function () {
                //alert("OK");
                self.cancelSupplier();
                self.getSuppliers();
            });
    }

    self.addSupplier = function() {
        ajaxHelper(baseUri + 'add_supplier', 'POST', null, [ko.toJS(self.tempsupplier)])
            .done(function () {
                //alert("OK");
                self.cancelSupplier();
                self.getSuppliers();
        });
    }

    self.deleteSupplier = function(rec) {
        ajaxHelper(baseUri + 'delete_supplier', 'POST').done(function (data) {
            //alert("OK");
            self.getSuppliers();
        });
    };

    // other data sets
    self.getParts = function() {
        ajaxHelper(baseUri + 'findall_part', 'POST').done(function (data) {
            self.parts(data);
        });
    }

    self.getSupplies = function() {
        ajaxHelper(baseUri + 'findall_supplies', 'POST').done(function (data) {
            self.supplies(data);
        });
    }

    self.findParts = function () {
        var arg = ko.toJS(self.partspattern);
        ajaxHelper(baseUri + 'find_part_by_name', 'POST', arg).done(function (data) {
            self.parts(data);
        });
    }

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
};

ko.applyBindings(new ViewModel());
