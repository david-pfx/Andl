// javascript broken out of original cshtml

// this sample uses knockout and a jquery REST interface

    var self = this;
//S1:Boolean to check wheather the operation is for Edit and New Record
var IsNewRecord = false;

self.Employees = ko.observableArray([]);

loadEmployees();

//S2:Method to Load all Employees by making call to WEB API GET method
function loadEmployees() {
    $.ajax({
        type: "GET",
        url: "/rest/emprest/employee",
        success: function (data) {
            //alert("Success");
            self.Employees(data);
        },
        error: function (err) {
            alert(err.status + ":" + err.statusText + ":" + err.responseText);
            //alert(err.status + " <--------------->");
        }
    });

};
//alert("Loading Data");

//S3:The Employee Object
function Employee(eno, ename, dname, desig, sal) {
    return {
        EmpNo: ko.observable(eno),
        EmpName: ko.observable(ename),
        DeptName: ko.observable(dname),
        Designation: ko.observable(desig),
        Salary: ko.observable(sal)
    }
};

//S4:The ViewModel where the Templates are initialized
var EmpViewModel = {
    readonlyTemplate: ko.observable("readonlyTemplate"),
    editTemplate: ko.observable()
};

//S5:Method ti decide the Current Template (readonlyTemplate or editTemplate)
EmpViewModel.currentTemplate = function (tmpl) {
    return tmpl === this.editTemplate() ? 'editTemplate' : this.readonlyTemplate();
}.bind(EmpViewModel);

//S6:Method to create a new Blabk entry When the Add New Record button is clicked
EmpViewModel.addnewRecord = function () {
    //alert("Add Called");
    self.Employees.push(new Employee(0, "", "", "", 0.0));
    IsNewRecord = true; //Set the Check for the New Record
};


//S7:Method to Save the Record (This is used for Edit and Add New Record)
EmpViewModel.saveEmployee = function (d) {

    var Emp = {};
    Emp.EmpNo = d.EmpNo;
    Emp.EmpName = d.EmpName;
    Emp.DeptName = d.DeptName;
    Emp.Designation = d.Designation;
    Emp.Salary = d.Salary;
    //Edit teh Record
    if (IsNewRecord === false) {
        $.ajax({
            type: "PUT",
            url: "/rest/emprest/employee/" + Emp.EmpNo,
            dataType: 'json',
            contentType: 'application/json',
            data: JSON.stringify([Emp]),
            //data: Emp,
            success: function (data) {
                alert("Record Updated Successfully");
                //alert("Record Updated Successfully " + data.status);
                EmpViewModel.reset();
            },
            error: function (err) {
                alert("Error Occures, Please Reload the Page and Try Again " + err.status + ":" + err.responseText);
                EmpViewModel.reset();
            }
        });
    }
    //The New Record
    if (IsNewRecord === true) {
        IsNewRecord = false;
        $.ajax({
            type: "POST",
            url: "/rest/emprest/employee",
            dataType: 'json',
            contentType: 'application/json',
            data: ko.toJSON([Emp]),
            //data: Emp,
            success: function (data) {
                alert("Record Added Successfully ");
                //alert("Record Added Successfully " + data.status);
                EmpViewModel.reset();
                loadEmployees();
            },
            error: function (err) {
                alert("Error Occures, Please Reload the Page and Try Again " + err.status);
                EmpViewModel.reset();
            }
        });
    }
};

//S8:Method to Delete the Record
EmpViewModel.deleteEmployee = function (d) {

    $.ajax({
        type: "DELETE",
        url: "/rest/emprest/Employee/" + d.EmpNo,
        success: function (data) {
            alert("Record Deleted Successfully " + data.status);
            EmpViewModel.reset();
            loadEmployees();
        },
        error: function (err) {
            alert("Error Occures, Please Reload the Page and Try Again " + err.status);
            EmpViewModel.reset();
        }
    });
};



//S9:Method to Reset the template
EmpViewModel.reset = function (t) {
    this.editTemplate("readonlyTemplate");
};


ko.applyBindings(EmpViewModel);





